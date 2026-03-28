import struct
import logging
import os

logger = logging.getLogger(__name__)


class elf():

        """
        ELF data format class for BackdoorFactory.
        We don't need the ENTIRE format.
        """    
        e_ident = {"EI_MAG": "\x7f" + "ELF",
                   "EI_CLASS": {0x01: "x86",
                                0x02: "x64"
                                },
                   "EI_DATA_little": 0x01,
                   "EI_DATA_big": 0x02,
                   "EI_VERSION": 0x01,
                   "EI_OSABI": {0x00: "System V",
                                0x01: "HP-UX",
                                0x02: "NetBSD",
                                0x03: "Linux",
                                0x06: "Solaris",
                                0x07: "AIX",
                                0x08: "IRIX",
                                0x09: "FreeBSD",
                                0x0C: "OpenBSD"
                                },
                   "EI_ABIVERSION": 0x00,
                   "EI_PAD": 0x07
                   }
        e_type = {0x01: "relocatable",
                  0x02: "executable",
                  0x03: "shared",
                  0x04: "core"
                  }
        e_machine = {0x02: "SPARC",
                     0x03: "x86",
                     0x14: "PowerPC",
                     0x28: "ARM",
                     0x32: "IA-64",
                     0x3E: "x86-64",
                     0xB7: "AArch64"
                     }
        e_version = 0x01


class elf_parse(object):

    def __init__(self, *args, **kwargs):
        logger.debug("In file {0}".format(str(os.path.abspath(__file__))))

        self.supported_types = {0x00:    # System V
                                [[0x01,  # 32bit
                                  0x02   # 64bit
                                  ],
                                 [0x03,  # x86
                                  0x28,  # ARM
                                  0x3E   # x64
                                  ]],
                                0x03:    # Linux
                                [[0x01,  # 32bit
                                  0x02   # 64bit
                                  ],
                                 [0x03,  # x86
                                  0x3E   # x64
                                  ]],
                                0x09:    # FreeBSD
                                [[0x01,  # 32bit
                                 # 0x02  # 64bit
                                  ],
                                 [0x03,  # x86
                                  # 0x3E # x64
                                  ]],
                                0x0C:    # OpenBSD
                                [[0x01,  # 32bit
                                 # 0x02   # 64bit
                                  ],
                                 [0x03,  # x86
                                  # 0x3E  # x64
                                  ]]
                                }

        for arg in args:
            setattr(self, arg, arg)
        for key, value in kwargs.items():
            setattr(self, key, value)
        if not hasattr(self, 'FILE'):
            logger.error("Provide a FILE as in FILE=your_file")
            return None

    def run(self):
        self.binary = self.FILE
        self.file_size = self.FILE.getbuffer().nbytes
        if not self.gather_file_info_elf():
            return False
        self.binary.seek(0)
        self.loaded_binary = self.binary
        return True

    def get_section_name(self, section_offset):
        """
        Get section names
        """
        if self.e_shstrndx not in self.sec_hdr:
            logger.warning("[!] Failed to get self.e_shstrndx. Fuzzing?")
            return False
        if self.sec_hdr[self.e_shstrndx]['sh_offset'] > self.file_size:
            logger.warning("[!] Fuzzing the sh_offset")
            return False
        self.binary.seek(self.sec_hdr[self.e_shstrndx]['sh_offset'] + section_offset, 0)
        name = b''
        j = b''
        while True:
            j = self.binary.read(1)
            if len(j) == 0:
                break
            elif j == b"\x00":
                break
            else:
                name += j
        # print "name:", name
        return name

    def set_section_name(self):
        """
        Set the section names
        """
        # how to find name section specifically
        for i in range(0, self.e_shstrndx + 1):
            self.sec_hdr[i]['name'] = self.get_section_name(self.sec_hdr[i]['sh_name'])
            if self.sec_hdr[i]['name'] is False:
                logger.warning("Failure in naming, fuzzing?")
                return False
        if self.sec_hdr[i]['name'] == ".text":
            # print "Found text section"
            self.text_section = i

    def gather_file_info_elf(self):
        logger.info("[*] Gathering file info")
        self.binary.seek(0)
        EI_MAG = self.binary.read(4)
        if EI_MAG != b'\x7fELF':
            logger.warning('Not an ELF file')
            return False
        self.EI_CLASS = struct.unpack("<B", self.binary.read(1))[0]
        self.EI_DATA = struct.unpack("<B", self.binary.read(1))[0]
        if self.EI_DATA == 0x01:
            # little endian
            self.endian = "<"
        else:
            # big self.endian
            self.endian = ">"
        self.EI_VERSION = struct.unpack('<B', self.binary.read(1))[0]
        self.EI_OSABI = struct.unpack('<B', self.binary.read(1))[0]
        self.EI_ABIVERSION = struct.unpack('<B', self.binary.read(1))[0]
        self.EI_PAD = struct.unpack(self.endian + "BBBBBBB", self.binary.read(7))[0]
        self.e_type = struct.unpack(self.endian + "H", self.binary.read(2))[0]
        self.e_machine = struct.unpack(self.endian + "H", self.binary.read(2))[0]
        self.e_version = struct.unpack(self.endian + "I", self.binary.read(4))[0]
        #print "EI_Class", self.EI_CLASS
        if self.EI_CLASS == 0x01:
            #"32 bit "
            self.e_entryLocOnDisk = self.binary.tell()
            self.e_entry = struct.unpack(self.endian + "I", self.binary.read(4))[0]
            #print hex(self.e_entry)
            self.e_phoff = struct.unpack(self.endian + "I", self.binary.read(4))[0]
            self.e_shoff = struct.unpack(self.endian + "I", self.binary.read(4))[0]
        else:
            #"64 bit "
            self.e_entryLocOnDisk = self.binary.tell()
            self.e_entry = struct.unpack(self.endian + "Q", self.binary.read(8))[0]
            self.e_phoff = struct.unpack(self.endian + "Q", self.binary.read(8))[0]
            self.e_shoff = struct.unpack(self.endian + "Q", self.binary.read(8))[0]
        self.VrtStrtngPnt = self.e_entry
        self.e_flags = struct.unpack(self.endian + "I", self.binary.read(4))[0]
        self.e_ehsize = struct.unpack(self.endian + "H", self.binary.read(2))[0]
        self.e_phentsize = struct.unpack(self.endian + "H", self.binary.read(2))[0]
        self.e_phnum = struct.unpack(self.endian + "H", self.binary.read(2))[0]
        self.e_shentsize = struct.unpack(self.endian + "H", self.binary.read(2))[0]
        self.e_shnum = struct.unpack(self.endian + "H", self.binary.read(2))[0]
        self.e_shstrndx = struct.unpack(self.endian + "H", self.binary.read(2))[0]

        '''
        redundant
        #section tables
        if self.e_phoff > os.path.getsize(self.FILE):
            logger.warning("[!] El fuzzero")
            return False
        self.binary.seek(self.e_phoff, 0)
        '''
        # header tables
        if self.e_shnum == 0:
            logger.warning("[*] More than 0xFF00 sections")
            logger.warning("[*] NOPE NOPE NOPE")
            return False

        else:
            self.real_num_sections = self.e_shnum

        if self.e_phoff > self.file_size:
            logger.warning("[*] e_phoff is greater than file size")
            return False

        self.binary.seek(self.e_phoff, 0)

        self.prog_hdr = {}
        for i in range(self.e_phnum):
            self.prog_hdr[i] = {}
            if self.EI_CLASS == 0x01:
                if self.e_phoff + (self.e_phnum * 4 * 8) > self.file_size:
                    logger.warning("[!] e_phoff and e_phnum is greater than the file size")
                    return False

                self.prog_hdr[i]['p_type'] = struct.unpack(self.endian + "I", self.binary.read(4))[0]
                self.prog_hdr[i]['p_offset'] = struct.unpack(self.endian + "I", self.binary.read(4))[0]
                self.prog_hdr[i]['p_vaddr'] = struct.unpack(self.endian + "I", self.binary.read(4))[0]
                self.prog_hdr[i]['p_paddr'] = struct.unpack(self.endian + "I", self.binary.read(4))[0]
                self.prog_hdr[i]['p_filesz'] = struct.unpack(self.endian + "I", self.binary.read(4))[0]
                self.prog_hdr[i]['p_memsz'] = struct.unpack(self.endian + "I", self.binary.read(4))[0]
                self.prog_hdr[i]['p_flags'] = struct.unpack(self.endian + "I", self.binary.read(4))[0]
                self.prog_hdr[i]['p_align'] = struct.unpack(self.endian + "I", self.binary.read(4))[0]
            else:
                if self.e_phoff + (self.e_phnum * ((4 * 2) + (6 * 8))) > self.file_size:
                    logger.warning("[!] e_phoff and e_phnum is greater than the file size")
                    return False

                self.prog_hdr[i]['p_type'] = struct.unpack(self.endian + "I", self.binary.read(4))[0]
                self.prog_hdr[i]['p_flags'] = struct.unpack(self.endian + "I", self.binary.read(4))[0]
                self.prog_hdr[i]['p_offset'] = struct.unpack(self.endian + "Q", self.binary.read(8))[0]
                self.prog_hdr[i]['p_vaddr'] = struct.unpack(self.endian + "Q", self.binary.read(8))[0]
                self.prog_hdr[i]['p_paddr'] = struct.unpack(self.endian + "Q", self.binary.read(8))[0]
                self.prog_hdr[i]['p_filesz'] = struct.unpack(self.endian + "Q", self.binary.read(8))[0]
                self.prog_hdr[i]['p_memsz'] = struct.unpack(self.endian + "Q", self.binary.read(8))[0]
                self.prog_hdr[i]['p_align'] = struct.unpack(self.endian + "Q", self.binary.read(8))[0]
            if self.prog_hdr[i]['p_type'] == 0x1 and self.prog_hdr[i]['p_vaddr'] < self.e_entry:
                self.offset_addr = self.prog_hdr[i]['p_vaddr']
                self.LocOfEntryinCode = self.e_entry - self.offset_addr
                #print "found the entry offset"

        if self.e_shoff > self.file_size:
            logger.warning("[!] e_shoff location is greater than file size")
            return False

        if self.e_shnum > self.file_size:
            logger.warning("[!] e_shnum is greater than file size")
            return False

        self.binary.seek(self.e_shoff, 0)
        self.sec_hdr = {}
        for i in range(self.e_shnum):
            self.sec_hdr[i] = {}
            if self.EI_CLASS == 0x01:
                if self.e_shoff + self.e_shnum * 4 * 10 > self.file_size:
                    logger.warning("[!] e_shnum is greater than file size")
                    return False    

                self.sec_hdr[i]['sh_name'] = struct.unpack(self.endian + "I", self.binary.read(4))[0]
                self.sec_hdr[i]['sh_type'] = struct.unpack(self.endian + "I", self.binary.read(4))[0]
                self.sec_hdr[i]['sh_flags'] = struct.unpack(self.endian + "I", self.binary.read(4))[0]
                self.sec_hdr[i]['sh_addr'] = struct.unpack(self.endian + "I", self.binary.read(4))[0]
                self.sec_hdr[i]['sh_offset'] = struct.unpack(self.endian + "I", self.binary.read(4))[0]
                self.sec_hdr[i]['sh_size'] = struct.unpack(self.endian + "I", self.binary.read(4))[0]
                self.sec_hdr[i]['sh_link'] = struct.unpack(self.endian + "I", self.binary.read(4))[0]
                self.sec_hdr[i]['sh_info'] = struct.unpack(self.endian + "I", self.binary.read(4))[0]
                self.sec_hdr[i]['sh_addralign'] = struct.unpack(self.endian + "I", self.binary.read(4))[0]
                self.sec_hdr[i]['sh_entsize'] = struct.unpack(self.endian + "I", self.binary.read(4))[0]
            else:
                if self.e_shoff + self.e_shnum * ((4 * 4) + (6 * 8))   > self.file_size:
                    logger.warning("[!] e_shnum is greater than file size")
                    return False
                self.sec_hdr[i]['sh_name'] = struct.unpack(self.endian + "I", self.binary.read(4))[0]
                self.sec_hdr[i]['sh_type'] = struct.unpack(self.endian + "I", self.binary.read(4))[0]
                self.sec_hdr[i]['sh_flags'] = struct.unpack(self.endian + "Q", self.binary.read(8))[0]
                self.sec_hdr[i]['sh_addr'] = struct.unpack(self.endian + "Q", self.binary.read(8))[0]
                self.sec_hdr[i]['sh_offset'] = struct.unpack(self.endian + "Q", self.binary.read(8))[0]
                self.sec_hdr[i]['sh_size'] = struct.unpack(self.endian + "Q", self.binary.read(8))[0]
                self.sec_hdr[i]['sh_link'] = struct.unpack(self.endian + "I", self.binary.read(4))[0]
                self.sec_hdr[i]['sh_info'] = struct.unpack(self.endian + "I", self.binary.read(4))[0]
                self.sec_hdr[i]['sh_addralign'] = struct.unpack(self.endian + "Q", self.binary.read(8))[0]
                self.sec_hdr[i]['sh_entsize'] = struct.unpack(self.endian + "Q", self.binary.read(8))[0]

        if self.set_section_name() is False:
            logger.warning("[!] Fuzzing sections")
            return False
        # if self.e_type != 0x2:
        #    logger.warning(f"[!] Only supporting executable elf e_types 2, things may get weird. Type found: {self.e_type}")

        return True

    def test(self):
        print(elf.e_ident["EI_MAG"])


if __name__ == "__main__":
    import sys
    import io
    test = elf_parse(FILE=io.BytesIO(open(sys.argv[1], 'r+b').read()))
    test.run()
