import os
import logging
from core import enum
import struct

logger = logging.getLogger(__name__)

class remove_signature:
    def __init__(self,BDF=None):
        logger.debug("In file {0}".format(str(os.path.abspath(__file__))))
        self.name = """remove_signature"""
        #maybe add supported methods
        self.description = """Update header to point to new entry"""
        self.requirements = {
                            }
        self.BDF = BDF
        self.supported_methods = ['pre_text_infection']

    def invoke_mode(self):

        return self.update_header()

    def update_header(self):

        # remove codesigning this could be moved to a post-processor
        if self.BDF.LC_CODE_SIGNATURE != {}:
            logger.info("[*] Removing self.LC_CODE_SIGNATURE command")
            self.BDF.macho_object['loaded_binary'].seek(self.BDF.macho_object['mach_hdrs'][self.BDF.key]['LOCLoadCmds'], 0)
            oldNumber = struct.unpack("<I", self.BDF.macho_object['loaded_binary'].read(4))[0]
            
            self.BDF.patch_instr[self.BDF.macho_object['mach_hdrs'][self.BDF.key]['LOCLoadCmds']] = struct.pack("<I", oldNumber - 1)

            oldsize = struct.unpack("<I", self.BDF.macho_object['loaded_binary'].read(4))[0]
            
            self.BDF.patch_instr[self.BDF.macho_object['mach_hdrs'][self.BDF.key]['LOCLoadCmds'] + 4] = struct.pack("<I", oldsize - 0x10)

        if self.BDF.LC_DYLIB_CODE_SIGN_DRS != {}:
            logger.info("[*] Removing self.BDF.LC_DYLIB_CODE_SIGN_DRS command")
            self.BDF.macho_object['loaded_binary'].seek(self.BDF.macho_object['mach_hdrs'][self.BDF.key]['LOCLoadCmds'], 0)
            oldNumber = struct.unpack("<I", self.BDF.macho_object['loaded_binary'].read(4))[0]
            
            self.BDF.patch_instr[self.BDF.macho_object[self.BDF.macho_object['mach_hdrs'][self.BDF.key]['LOCLoadCmds']]] = struct.pack("<I", oldNumber - 1)

            oldsize = struct.unpack("<I", self.BDF.macho_object['loaded_binary'].read(4))[0]
            
            self.BDF.patch_instr[self.BDF.macho_object['mach_hdrs'][self.BDF.key]['LOCLoadCmds']] = struct.pack("<I", oldsize - 0x10)

        return True