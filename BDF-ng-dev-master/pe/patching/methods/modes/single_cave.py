import os
import logging
import io
import re
from pe.core import core
logger = logging.getLogger(__name__)

class single_cave:

    def __init__(self, BDF=None):
        logger.debug("In file {0}".format(str(os.path.abspath(__file__))))
        self.name = """single_cave"""
        #maybe add supported methods
        self.description = """Put the payload in a single code cave already in the binary (creates RWX section)."""
        self.requirements = {}
        self.supported_methods = ['jmp_at_entrypoint', 'call_at_entrypoint']
        self.BDF = BDF
        
    def invoke_mode(self):
        # There are two choices here, manual and automatic
        #   
        #  1st find caves
        #  - Create a function that is shared to find caves based on a list of shellcode strings (by fileformat)
        #  2nd Pick caves
        #  - if manual mode (invoke manual mode) - Create this function, shared (by fileformat)
        #  - if automatic (invoke auto mode) - Create this function, shared (by fileformat)
        #  - return picked_caves
        return self.find_cave()

    def find_cave(self):
        self.BDF.pe_object['cave_jumping'] = False
        
        self.BDF.pe_object['len_allshells'] = (self.BDF.pe_object['shellcode_length'], )
        self.BDF.SIZE_CAVE_TO_FIND = self.BDF.pe_object['shellcode_length']

        modifier = core.core(self.BDF)

        if hasattr(modifier, self.BDF.options['MODIFIER'].lower()):
            logger.debug(f"Modifier: {self.BDF.options['MODIFIER']}")
            self.BDF.found_modifier = getattr(modifier, self.BDF.options['MODIFIER'].lower())
        else:
            logger.error('No found modifier')
            return False

        if self.BDF.found_modifier() != True:
            return False

        logger.debug(f"Caves Picked {self.BDF.pe_object['CavesPicked']}")
        
        return True

    def get_patch_instr(self):
        for i, item in self.BDF.pe_object['CavesPicked'].items():
                if i == 0:
                    self.BDF.patch_instr[int(self.BDF.pe_object['CavesPicked'][i][1], 16)] = self.BDF.pe_object['completeShellcode']
        
