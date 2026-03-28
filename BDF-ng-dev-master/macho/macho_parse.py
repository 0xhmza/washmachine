import struct
import os
import logging
logger = logging.getLogger(__name__)


class macho_parse(object):
    '''
    Take a macho file and return a stringIO object and the processed header infos
    '''

    def __init__(self, *args, **kwargs):
        logger.debug("In file {0}".format(str(os.path.abspath(__file__))))

        for arg in args:
            setattr(self, arg, arg)
        for key, value in kwargs.items():
            setattr(self, key, value)
        if not hasattr(self, 'FILE'):
            logger.error("Provide a FILE as in FILE=your_file")
            return None

        self.fat_hdrs = {}
        self.mach_hdrs = {}
        self.load_cmds = {}
        self.ImpValues = {}
        self.supported_CPU_TYPES = [0x7,         # i386
                                    0x01000007,  # x64
                                    0x100000c,   # arm64e
                                    ]

    def run(self):
        logger.info("[*] Checking file support")
        self.binary = self.FILE

        check = self.get_structure()
        if check is False:
            self.supported = False

        for key, value in self.load_cmds.items():
            self.ImpValues[key] = self.find_Needed_Items(value)
            if self.ImpValues[key]['text_segment'] == {}:
                logger.info('[!] Not a proper Mach-O file')
                self.supported = False

        self.loaded_binary = self.binary

        self.loaded_binary.seek(0)

        return True

    def get_structure(self):
        '''This function grabs necessary data for the mach-o format'''
        self.binary_header = self.binary.read(4)
        if self.binary_header == b"\xca\xfe\xba\xbe":
            logger.info('[*] Fat File detected')
            self.FAT_FILE = True
            ArchNo = struct.unpack(">I", self.binary.read(4))[0]
            for arch in range(ArchNo):
                self.fat_hdrs[arch] = self.fat_header()
            for hdr, value in self.fat_hdrs.items():
                if int(value['CPU Type'], 16) in self.supported_CPU_TYPES:
                    self.binary.seek(int(value['Offset'], 16), 0)
                    self.mach_hdrs[hdr] = self.mach_header()
                    self.load_cmds[hdr] = self.parse_loadcommands(self.mach_hdrs[hdr])
            if self.mach_hdrs is False:
                return False
        else:
            #Not Fat Header
            self.binary.seek(0)
            self.mach_hdrs[0] = self.mach_header()
            self.load_cmds[0] = self.parse_loadcommands(self.mach_hdrs[0])

    def fat_header(self):
        header = {}
        header["CPU Type"] = hex(struct.unpack(">I", self.binary.read(4))[0])
        header["CPU SubType"] = hex(struct.unpack(">I", self.binary.read(4))[0])
        header["Offset"] = hex(struct.unpack(">I", self.binary.read(4))[0])
        header["Size"] = hex(struct.unpack(">I", self.binary.read(4))[0])
        header["Align"] = hex(struct.unpack(">I", self.binary.read(4))[0])
        return header

    def mach_header(self):
        header = {}
        header['beginningOfHeader'] = self.binary.tell()
        try:
            header['MagicNumber'] = hex(struct.unpack("<I", self.binary.read(4))[0])
        except:
            logger.info("[!] Not a properly formated Mach-O file")
            return False
        header['CPU Type'] = hex(struct.unpack("<I", self.binary.read(4))[0])
        header['CPU SubType'] = hex(struct.unpack("<I", self.binary.read(4))[0])
        header['File Type'] = hex(struct.unpack("<I", self.binary.read(4))[0])
        header['LOCLoadCmds'] = self.binary.tell()
        header['Load Cmds'] = hex(struct.unpack("<I", self.binary.read(4))[0])
        header['LOCSizeLdCmds'] = self.binary.tell()
        header['Size Load Cmds'] = hex(struct.unpack("<I", self.binary.read(4))[0])
        header['Flags'] = hex(struct.unpack("<I", self.binary.read(4))[0])
        if header['MagicNumber'] == '0xfeedfacf':
            header['Reserved'] = hex(struct.unpack("<I", self.binary.read(4))[0])
        header['endOfHeader'] = self.binary.tell()
        return header

    def parse_loadcommands(self, someHdrs):
        #logger.info(int(someHdrs['Load Cmds'], 16))
        overall_cmds = []
        for section in range(int(someHdrs['Load Cmds'], 16)):
            cmds = {}
            cmds['Command'] = struct.unpack("<I", self.binary.read(4))[0]
            cmds['CommandSize'] = struct.unpack("<I", self.binary.read(4))[0]
            cmds['LOCInFIle'] = self.binary.tell()
            cmds['DATA'] = self.binary.read(int(cmds['CommandSize']) - 8)
            cmds['last_cmd'] = self.binary.tell()
            overall_cmds.append(cmds)

        return overall_cmds

    def find_Needed_Items(self, theCmds):
        '''
        This method returns a dict with commands that we need
        for mach-o patching
        '''
        _tempDict = {}
        text_segment = {}
        text_section = {}
        LC_MAIN = {}
        LC_UNIXTREAD = {}
        LC_CODE_SIGNATURE = {}
        LC_DYLIB_CODE_SIGN_DRS = {}

        locationInFIle = 0
        last_cmd = 0
        for item in theCmds:
            locationInFIle = item['LOCInFIle']
            if item['DATA'][0:6] == b"__TEXT" and item['Command'] == 0x01:
                text_segment = {
                    'segname': item['DATA'][0:0x10],
                    'VMAddress': item['DATA'][0x10:0x14],
                    'VMSize': item['DATA'][0x14:0x18],
                    'File Offset': item['DATA'][0x18:0x1C],
                    'File Size': item['DATA'][0x1C:0x20],
                    'MaxVMProt': item['DATA'][0x20:0x24],
                    'InitalVMProt': item['DATA'][0x24:0x28],
                    'NumberOfSections': item['DATA'][0x28:0x2C],
                    'Flags': item['DATA'][0x2C:0x30]
                }

                count = struct.unpack("<I", text_segment['NumberOfSections'])[0]
                i = 0
                while count > 0:
                    if b'__text' in item['DATA'][0x30 + i:0x40 + i]:
                        text_section = {
                            'sectionName': item['DATA'][0x30 + i:0x40 + i],
                            'segmentName': item['DATA'][0x40 + i:0x50 + i],
                            'Address': item['DATA'][0x50 + i:0x54 + i],
                            'LOCAddress': locationInFIle + 0x50 + i,
                            'Size': item['DATA'][0x54 + i:0x58 + i],
                            'LOCTextSize': locationInFIle + 0x54 + i,
                            'Offset': item['DATA'][0x58 + i:0x5c + i],
                            'LocTextOffset': locationInFIle + 0x58 + i,
                            'Alignment': item['DATA'][0x5c + i:0x60 + i],
                            'Relocations': item['DATA'][0x60 + i:0x64 + i],
                            'NumberOfRelocs': item['DATA'][0x64 + i:0x68 + i],
                            'Flags': item['DATA'][0x68 + i:0x6c + i],
                            'Reserved1': item['DATA'][0x6c + i:0x70 + i],
                            'Reserved2': item['DATA'][0x70 + i:0x74 + i],
                        }
                        break
                    else:
                        count -= 1
                        i += 0x40

            elif item['DATA'][0:6] == b"__TEXT" and item['Command'] == 0x19:
                text_segment = {
                    'segname': item['DATA'][0:0x10],
                    'VMAddress': item['DATA'][0x10:0x18],
                    'VMSize': item['DATA'][0x18:0x20],
                    'File Offset': item['DATA'][0x20:0x28],
                    'File Size': item['DATA'][0x28:0x30],
                    'MaxVMProt': item['DATA'][0x30:0x34],
                    'InitalVMProt': item['DATA'][0x34:0x38],
                    'NumberOfSections': item['DATA'][0x38:0x3C],
                    'Flags': item['DATA'][0x3c:0x40]
                }
                count = struct.unpack("<I", text_segment['NumberOfSections'])[0]
                i = 0
                while count > 0:

                    if b'__text' in item['DATA'][0x40 + i:0x50 + i]:
                        text_section = {
                            'sectionName': item['DATA'][0x40 + i:0x50 + i],
                            'segmentName': item['DATA'][0x50 + i:0x60 + i],
                            'Address': item['DATA'][0x60 + i:0x68 + i],
                            'LOCAddress': locationInFIle + 0x60 + i,
                            'Size': item['DATA'][0x68 + i:0x70 + i],
                            'LOCTextSize': locationInFIle + 0x68 + i,
                            'Offset': item['DATA'][0x70 + i:0x74 + i],
                            'LocTextOffset': locationInFIle + 0x70 + i,
                            'Alignment': item['DATA'][0x74 + i:0x78 + i],
                            'Relocations': item['DATA'][0x78 + i:0x7c + i],
                            'NumberOfRelocs': item['DATA'][0x7c + i:0x80 + i],
                            'Flags': item['DATA'][0x80 + i:0x84 + i],
                            'Reserved1': item['DATA'][0x84 + i:0x88 + i],
                            'Reserved2': item['DATA'][0x88 + i:0x8c + i],
                            'Reserved3': item['DATA'][0x8c + i:0x90 + i],
                        }

                        break
                    else:
                        count -= 1
                        i += 0x4c

            if item['Command'] == 0x80000028:
                LC_MAIN = {
                    'LOCEntryOffset': locationInFIle,
                    'EntryOffset': item['DATA'][0x0:0x8],
                    'StackSize': item['DATA'][0x8:0x16]
                }
            elif item['Command'] == 0x00000005 and struct.unpack("<I", item['DATA'][0x0:0x4])[0] == 0x01:
                LC_UNIXTREAD = {
                    'LOCEntryOffset': locationInFIle,
                    'Flavor': item['DATA'][0x00:0x04],
                    'Count': item['DATA'][0x04:0x08],
                    'eax': item['DATA'][0x08:0x0C],
                    'ebx': item['DATA'][0x0C:0x10],
                    'ecx': item['DATA'][0x10:0x14],
                    'edx': item['DATA'][0x14:0x18],
                    'edi': item['DATA'][0x18:0x1C],
                    'esi': item['DATA'][0x1C:0x20],
                    'ebp': item['DATA'][0x20:0x24],
                    'esp': item['DATA'][0x24:0x28],
                    'ss': item['DATA'][0x28:0x2C],
                    'eflags': item['DATA'][0x2C:0x30],
                    'LOCeip': locationInFIle + 0x30,
                    'eip': item['DATA'][0x30:0x34],
                    'cs': item['DATA'][0x34:0x38],
                    'ds': item['DATA'][0x38:0x3C],
                    'es': item['DATA'][0x3C:0x40],
                    'fs': item['DATA'][0x40:0x44],
                    'gs': item['DATA'][0x44:0x48],
                }
            elif item['Command'] == 0x00000005 and struct.unpack("<I", item['DATA'][0x0:0x4])[0] == 0x04:
                LC_UNIXTREAD = {
                    'LOCEntryOffset': locationInFIle,
                    'Flavor': item['DATA'][0x00:0x04],
                    'Count': item['DATA'][0x04:0x08],
                    'rax': item['DATA'][0x08:0x10],
                    'rbx': item['DATA'][0x10:0x18],
                    'rcx': item['DATA'][0x18:0x20],
                    'rdx': item['DATA'][0x20:0x28],
                    'rdi': item['DATA'][0x28:0x30],
                    'rsi': item['DATA'][0x30:0x38],
                    'rbp': item['DATA'][0x38:0x40],
                    'rsp': item['DATA'][0x40:0x48],
                    'r8': item['DATA'][0x48:0x50],
                    'r9': item['DATA'][0x50:0x58],
                    'r10': item['DATA'][0x58:0x60],
                    'r11': item['DATA'][0x60:0x68],
                    'r12': item['DATA'][0x68:0x70],
                    'r13': item['DATA'][0x70:0x78],
                    'r14': item['DATA'][0x78:0x80],
                    'r15': item['DATA'][0x80:0x88],
                    'LOCrip': locationInFIle + 0x88,
                    'rip': item['DATA'][0x88:0x90],
                    'rflags': item['DATA'][0x90:0x98],
                    'cs': item['DATA'][0x98:0xA0],
                    'fs': item['DATA'][0xA0:0xA8],
                    'gs': item['DATA'][0xA8:0xB0],
                }

            if item['Command'] == 0x000001D:
                LC_CODE_SIGNATURE = {
                    'Data Offset': item['DATA'][0x0:0x4],
                    'Data Size': item['DATA'][0x0:0x8],
                }

            if item['Command'] == 0x0000002B:
                LC_DYLIB_CODE_SIGN_DRS = {
                    'Data Offset': item['DATA'][0x0:0x4],
                    'Data Size': item['DATA'][0x0:0x8],
                }

            if item['last_cmd'] > last_cmd:
                last_cmd = item['last_cmd']

        _tempDict = {'text_segment': text_segment, 'text_section': text_section,
                     'LC_MAIN': LC_MAIN, 'LC_UNIXTREAD': LC_UNIXTREAD,
                     'LC_CODE_SIGNATURE': LC_CODE_SIGNATURE,
                     'LC_DYLIB_CODE_SIGN_DRS': LC_DYLIB_CODE_SIGN_DRS,
                     'last_cmd': last_cmd
                     }

        return _tempDict

    
