import struct
import logging
import os
logger = logging.getLogger(__name__)


class pre_text_core():

    def __init__(self, BDF=None):
        self.BDF = BDF    
        logger.debug("In file {0}".format(str(os.path.abspath(__file__))))
        self.name = "text_split_core"
        self.description = """Supports splitting the text section for payload injection Intel x64"""
        self.requirements = {}

    def get_jmp_location(self):
        logger.info("[*] Patching arm64e Mach-O Binary")

        self.BDF.jumpLocation = 0x0

        if self.BDF.LC_UNIXTREAD != {}:
            logger.info("[*] ...with self.BDF.LC_UNIXTREAD format")
            #print 'self.BDF.LC_UNIXTREAD', struct.unpack("<Q", self.BDF.LC_UNIXTREAD['rip'])[0], struct.unpack("<Q", text_section['Address'])[0]
            if struct.unpack("<Q", self.BDF.LC_UNIXTREAD['rip'])[0] - struct.unpack("<Q", self.BDF.text_section['Address'])[0] != 0x0:
                self.BDF.jumpLocation = struct.unpack("<Q", self.BDF.LC_UNIXTREAD['rip'])[0] - struct.unpack("<Q", self.BDF.text_section['Address'])[0]
        else:
            logger.info("[*] ...with self.BDF.LC_MAIN format")
            #print(hex(struct.unpack("<Q", self.BDF.LC_MAIN['EntryOffset'])[0]), hex(struct.unpack("<I", self.BDF.text_section['Offset'])[0]))
            if struct.unpack("<Q", self.BDF.LC_MAIN['EntryOffset'])[0] - struct.unpack("<I", self.BDF.text_section['Offset'])[0] != 0x0:
                self.BDF.jumpLocation = struct.unpack("<Q", self.BDF.LC_MAIN['EntryOffset'])[0] - struct.unpack("<I", self.BDF.text_section['Offset'])[0]

        return True

    def update_header(self):

        self.BDF.patch_instr[self.BDF.startingLocation] = self.BDF.macho_object['shellcode']

        self.BDF.macho_object['loaded_binary'].seek(self.BDF.text_section['LOCAddress'], 0)
        newAddress = struct.unpack("<Q", self.BDF.text_section['Address'])[0] - self.BDF.macho_object['shellcode_length']

        self.BDF.patch_instr[self.BDF.text_section['LOCAddress']] = struct.pack("<Q", newAddress)

        newSize = struct.unpack("<Q", self.BDF.text_section['Size'])[0] + self.BDF.macho_object['shellcode_length']

        self.BDF.patch_instr['newSize'] = struct.pack("<Q", newSize)

        newOffset = struct.unpack("<I", self.BDF.text_section['Offset'])[0] - self.BDF.macho_object['shellcode_length']

        self.BDF.patch_instr['newOffset'] = struct.pack("<I", newOffset)

        if self.BDF.LC_UNIXTREAD != {}:

            self.BDF.patch_instr[self.BDF.LC_UNIXTREAD['LOCeip']] = struct.pack("<Q", newAddress)
        elif self.BDF.LC_MAIN != {}:

            self.BDF.patch_instr[self.BDF.LC_MAIN['LOCEntryOffset']] = struct.pack("<Q", newOffset)

        self.BDF.macho_object['loaded_binary'].seek(0)

        return True
