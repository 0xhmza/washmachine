import struct
import logging
import os

logger = logging.getLogger(__name__)


class pe_parse(object):

    '''
    Literally take only the pefile and return a processed PEFile
    and the PEFILE in memory as a stringIO object

    '''

    def __init__(self, *args, **kwargs):
        logger.debug("In file {0}".format(str(os.path.abspath(__file__))))

        self.MachineTypes = {'0x0': 'AnyMachineType',
                             '0x1d3': 'Matsushita AM33',
                             '0x8664': 'x64',
                             '0x1c0': 'ARM LE',
                             '0x1c4': 'ARMv7',
                             '0xaa64': 'ARMv8 x64',
                             '0xebc': 'EFIByteCode',
                             '0x14c': 'Intel x86',
                             '0x200': 'Intel Itanium',
                             '0x9041': 'M32R',
                             '0x266': 'MIPS16',
                             '0x366': 'MIPS w/FPU',
                             '0x466': 'MIPS16 w/FPU',
                             '0x1f0': 'PowerPC LE',
                             '0x1f1': 'PowerPC w/FP',
                             '0x166': 'MIPS LE',
                             '0x1a2': 'Hitachi SH3',
                             '0x1a3': 'Hitachi SH3 DSP',
                             '0x1a6': 'Hitachi SH4',
                             '0x1a8': 'Hitachi SH5',
                             '0x1c2': 'ARM or Thumb -interworking',
                             '0x169': 'MIPS little-endian WCE v2'
                             }

        self.supported_types = ['Intel x86', 'x64']

        # self.binary = BytesIO()

        for arg in args:
            setattr(self, arg, arg)
        for key, value in kwargs.items():
            setattr(self, key, value)
        if not hasattr(self, 'FILE'):
            logger.error("Provide a FILE as in FILE=your_file")
            return None
        if not hasattr(self, 'DISK_OFFSET'):
            logger.error("No DISK_OFFSET provided using 0")
            self.DISK_OFFSET = 0

    def run(self):
        self.binary = self.FILE

        if not self.gather_file_info_win():
            return False
        self.binary.seek(0)
        self.loaded_binary = self.binary
        return True

    def gather_file_info_win(self):
        """
        Gathers necessary PE header information to backdoor
        a file and returns a dict of file info.
        Takes a open file handle of self.binary
        """

        # This needs to be fuzzed
        if self.binary.read(2) != b"\x4d\x5a":
            logger.error(f"{self.FILE} not a PE File")
            return False

        self.binary.seek(0)
        self.binary.seek(int('3C', 16))
        logger.info("[*] Gathering file info")
        self.filename = self.FILE
        self.buffer = 0
        self.JMPtoCodeAddress = 0
        self.LocOfEntryinCode_Offset = self.DISK_OFFSET
        self.dis_frm_pehdrs_sectble = 248
        self.pe_header_location = struct.unpack('<i', self.binary.read(4))[0]
        # Start of COFF
        self.COFF_Start = self.pe_header_location + 4
        self.binary.seek(self.COFF_Start)
        self.MachineType = struct.unpack('<H', self.binary.read(2))[0]
        for mactype, name in self.MachineTypes.items():
            if int(mactype, 16) == self.MachineType:
                logger.debug(f"MachineType is:{name}")
        self.binary.seek(self.COFF_Start + 2, 0)
        self.NumberOfSections = struct.unpack('<H', self.binary.read(2))[0]
        self.TimeDateStamp = struct.unpack('<I', self.binary.read(4))[0]
        self.binary.seek(self.COFF_Start + 16, 0)
        self.SizeOfOptionalHeader = struct.unpack('<H', self.binary.read(2))[0]
        self.Characteristics = struct.unpack('<H', self.binary.read(2))[0]
        if (self.Characteristics >> 8 ) / 32 == 1:
            self.IsDLL = True
        else:
            self.IsDLL = False
        logger.debug(f"{self.IsDLL=}, {self.Characteristics=}")
        # End of COFF
        self.OptionalHeader_start = self.COFF_Start + 20

        # if self.SizeOfOptionalHeader:
            # Begin Standard Fields section of Optional Header
        self.binary.seek(self.OptionalHeader_start)
        self.Magic = struct.unpack('<H', self.binary.read(2))[0]
        self.MajorLinkerVersion = struct.unpack("!B", self.binary.read(1))[0]
        self.MinorLinkerVersion = struct.unpack("!B", self.binary.read(1))[0]
        self.SizeOfCode = struct.unpack("<I", self.binary.read(4))[0]
        self.SizeOfInitializedData = struct.unpack("<I", self.binary.read(4))[0]
        self.SizeOfUninitializedData = struct.unpack("<I", self.binary.read(4))[0]
        self.AddressOfEntryPoint = struct.unpack('<I', self.binary.read(4))[0]
        self.PatchLocation = self.AddressOfEntryPoint
        self.BaseOfCode = struct.unpack('<I', self.binary.read(4))[0]
        if self.Magic != 0x20B:
            self.BaseOfData = struct.unpack('<I', self.binary.read(4))[0]
        # End Standard Fields section of Optional Header
        # Begin Windows-Specific Fields of Optional Header
        if self.Magic == 0x20B:
            self.ImageBase = struct.unpack('<Q', self.binary.read(8))[0]
        else:
            self.ImageBase = struct.unpack('<I', self.binary.read(4))[0]
        self.SectionAlignment = struct.unpack('<I', self.binary.read(4))[0]
        self.FileAlignment = struct.unpack('<I', self.binary.read(4))[0]
        self.MajorOperatingSystemVersion = struct.unpack('<H', self.binary.read(2))[0]
        self.MinorOperatingSystemVersion = struct.unpack('<H', self.binary.read(2))[0]
        self.MajorImageVersion = struct.unpack('<H', self.binary.read(2))[0]
        self.MinorImageVersion = struct.unpack('<H', self.binary.read(2))[0]
        self.MajorSubsystemVersion = struct.unpack('<H', self.binary.read(2))[0]
        self.MinorSubsystemVersion = struct.unpack('<H', self.binary.read(2))[0]
        self.Win32VersionValue = struct.unpack('<I', self.binary.read(4))[0]
        self.SizeOfImageLoc = self.binary.tell()
        self.SizeOfImage = struct.unpack('<I', self.binary.read(4))[0]
        self.SizeOfHeaders = struct.unpack('<I', self.binary.read(4))[0]
        self.CheckSumLoC = self.binary.tell()
        self.CheckSum = struct.unpack('<I', self.binary.read(4))[0]
        self.Subsystem = struct.unpack('<H', self.binary.read(2))[0]
        self.DllCharacteristics = struct.unpack('<H', self.binary.read(2))[0]

        if self.Magic == 0x20B:
            self.SizeOfStackReserve = struct.unpack('<Q', self.binary.read(8))[0]
            self.SizeOfStackCommit = struct.unpack('<Q', self.binary.read(8))[0]
            self.SizeOfHeapReserve = struct.unpack('<Q', self.binary.read(8))[0]
            self.SizeOfHeapCommit = struct.unpack('<Q', self.binary.read(8))[0]

        else:
            self.SizeOfStackReserve = struct.unpack('<I', self.binary.read(4))[0]
            self.SizeOfStackCommit = struct.unpack('<I', self.binary.read(4))[0]
            self.SizeOfHeapReserve = struct.unpack('<I', self.binary.read(4))[0]
            self.SizeOfHeapCommit = struct.unpack('<I', self.binary.read(4))[0]
        self.LoaderFlags = struct.unpack('<I', self.binary.read(4))[0]  # zero
        self.NumberofRvaAndSizes = struct.unpack('<I', self.binary.read(4))[0]
        # End Windows-Specific Fields of Optional Header

        # Begin Data Directories of Optional Header
        self.ExportTableLOCInPeOptHdrs = self.binary.tell()
        self.ExportDirectoryTableRVA = struct.unpack('<I', self.binary.read(4))[0]
        self.ExportTableSize = struct.unpack('<I', self.binary.read(4))[0]
        self.ImportTableLOCInPEOptHdrs = self.binary.tell()
        # ImportTable SIZE|LOC
        self.ImportTableRVA = struct.unpack('<I', self.binary.read(4))[0]
        self.ImportTableSize = struct.unpack('<I', self.binary.read(4))[0]
        self.ResourceTable = struct.unpack('<Q', self.binary.read(8))[0]
        self.ExceptionTable = struct.unpack('<Q', self.binary.read(8))[0]
        self.CertTableLOC = self.binary.tell()
        self.CertLOC = struct.unpack("<I", self.binary.read(4))[0]
        self.CertSize = struct.unpack("<I", self.binary.read(4))[0]
        self.BaseReLocationTable = struct.unpack('<Q', self.binary.read(8))[0]
        self.Debug = struct.unpack('<Q', self.binary.read(8))[0]
        self.Architecture = struct.unpack('<Q', self.binary.read(8))[0]  # zero
        self.GlobalPrt = struct.unpack('<Q', self.binary.read(8))[0]
        self.TLS_Table = struct.unpack('<Q', self.binary.read(8))[0]
        self.LoadConfigTableRVA = struct.unpack('<I', self.binary.read(4))[0]
        self.LoadConfigTableSize = struct.unpack('<I', self.binary.read(4))[0]
        # self.LoadConfigTable = struct.unpack('<Q', self.binary.read(8))[0]
        self.BoundImportLocation = self.binary.tell()
        self.BoundImport = struct.unpack('<Q', self.binary.read(8))[0]
        self.binary.seek(self.BoundImportLocation)
        self.BoundImportLOCinCode = struct.unpack('<I', self.binary.read(4))[0]
        self.BoundImportSize = struct.unpack('<I', self.binary.read(4))[0]
        self.IAT = struct.unpack('<Q', self.binary.read(8))[0]
        self.DelayImportDesc = struct.unpack('<Q', self.binary.read(8))[0]
        self.CLRRuntimeHeader = struct.unpack('<Q', self.binary.read(8))[0]
        self.Reserved = struct.unpack('<Q', self.binary.read(8))[0]  # zero
        self.BeginSections = self.binary.tell()
        self.rawToVirtualOffset = 0x0

        if self.NumberOfSections != 0 and not hasattr(self, 'Section'):
            self.Sections = []
            for section in range(self.NumberOfSections):
                sectionValues = []
                # 0. section Name
                section_name = self.binary.read(8)

                sectionValues.append(section_name)
                # 1. VirtualSize
                setattr(self, section_name.decode('utf-8') + '_VirtualSize_LOC', self.binary.tell())
                sectionValues.append(struct.unpack('<I', self.binary.read(4))[0])
                # 2. VirtualAddress
                # setattr(self, section_name.decode('utf-8') + '_VirtualAddress_LOC', self.binary.tell())
                sectionValues.append(struct.unpack('<I', self.binary.read(4))[0])
                # 3. SizeOfRawData
                sectionValues.append(struct.unpack('<I', self.binary.read(4))[0])
                # 4. PointerToRawData
                sectionValues.append(struct.unpack('<I', self.binary.read(4))[0])
                # 5. PointerToRelocations
                sectionValues.append(struct.unpack('<I', self.binary.read(4))[0])
                # 6. PointerToLinenumbers
                sectionValues.append(struct.unpack('<I', self.binary.read(4))[0])
                # 7. NumberOfRelocations
                sectionValues.append(struct.unpack('<H', self.binary.read(2))[0])
                # 8. NumberOfLinenumbers
                sectionValues.append(struct.unpack('<H', self.binary.read(2))[0])
                # 9. SectionFlags/Characteristics
                sectionValues.append(struct.unpack('<I', self.binary.read(4))[0])

                self.Sections.append(sectionValues)

                if b'UPX1'.lower() in sectionValues[0].lower():
                    logger.info("[*] UPX packed, continuing...")

                if (b'.text\x00\x00\x00' == sectionValues[0] or
                    b'AUTO\x00\x00\x00\x00' == sectionValues[0] or
                    b'UPX1\x00\x00\x00\x00' == sectionValues[0] or
                    b'CODE\x00\x00\x00\x00' == sectionValues[0]):
                    self.textSectionName = sectionValues[0]
                    self.textVirtualSize = sectionValues[1]
                    self.textVirtualAddress = sectionValues[2]
                    self.textSizeRawData = sectionValues[3]
                    self.textPointerToRawData = sectionValues[4]

                    self.LocOfEntryinCode = (self.AddressOfEntryPoint -
                                                       self.textVirtualAddress +
                                                       self.textPointerToRawData +
                                                       self.LocOfEntryinCode_Offset)
                elif b'.rsrc\x00\x00\x00' == sectionValues[0]:
                    self.rsrcSectionName = sectionValues[0]
                    self.rsrcVirtualSize = sectionValues[1]
                    self.rsrcVirtualAddress = sectionValues[2]
                    self.rsrcSizeRawData = sectionValues[3]
                    self.rsrcPointerToRawData = sectionValues[4]
                    self.exportRawToVirtualOffset = - sectionValues[2] + sectionValues[4]
                logger.debug(sectionValues)
            # I could add in checks here to support an out of order PE file;
            #  However if here were multiple sections that were RE, RWE, it would be
            #  difficult to get it right in a purposefully mangled binary.
            #  Perhaps if entrypoint is in RE section that is text section? But still.
            #  That could be spoofed and it returns to another RE section.

            if not hasattr(self, "textSectionName"):
                self.LocOfEntryinCode = (self.AddressOfEntryPoint -
                                                   self.LocOfEntryinCode_Offset)  
            self.VirtualAddress = self.SizeOfImage

        else:
            self.LocOfEntryinCode = (self.AddressOfEntryPoint -
                                               self.LocOfEntryinCode_Offset)

        self.VrtStrtngPnt = (self.AddressOfEntryPoint +
                                       self.ImageBase)
        self.binary.seek(self.BoundImportLOCinCode)
        self.ImportTableALL = self.binary.read(self.BoundImportSize)
        self.NewIATLoc = self.BoundImportLOCinCode + 40
        self.LastCaveAddress = 0
        # FIX THIS
        # ParseLoadConfigTable
        self.LoadConfigTablePresent = False
        self.LoadConfigTable_OFFSET = 0
        for section in reversed(self.Sections):
            if self.LoadConfigTableRVA >= section[2]:
                #go to exact export directory location
                self.LoadConfigTablePresent = True
                self.binary.seek((self.LoadConfigTableRVA - section[2]) + section[4])
                # This works for exports
                self.LoadConfigTable_OFFSET = - section[2] + section[4]
                break

        if self.LoadConfigTablePresent is True:

            # This is for 32bit... need x64
            self.LoadConfigDirectory_Size = struct.unpack('<I', self.binary.read(4))[0]
            self.LoadConfigDirectory_TimeDataStamp = struct.unpack('<I', self.binary.read(4))[0]
            self.LoadConfigDirectory_MajorVersion = struct.unpack('<H', self.binary.read(2))[0]
            self.LoadConfigDirectory_MinorVersion = struct.unpack('<H', self.binary.read(2))[0]
            self.LoadConfigDirectory_GFC = struct.unpack('<I', self.binary.read(4))[0]
            self.LoadConfigDirectory_GFS = struct.unpack('<I', self.binary.read(4))[0]
            self.LoadConfigDirectory_CSDT = struct.unpack('<I', self.binary.read(4))[0]

            if self.Magic == 0x20B:
                #  winx64
                self.LoadConfigDirectory_DFBT = struct.unpack('<Q', self.binary.read(8))[0]
                self.LoadConfigDirectory_DTFT = struct.unpack('<Q', self.binary.read(8))[0]
                self.LoadConfigDirectory_LPTV = struct.unpack('<Q', self.binary.read(8))[0]
                self.LoadConfigDirectory_MAS = struct.unpack('<Q', self.binary.read(8))[0]
                self.LoadConfigDirectory_VMT = struct.unpack('<Q', self.binary.read(8))[0]
                self.LoadConfigDirectory_PAM = struct.unpack('<Q', self.binary.read(8))[0]
                self.LoadConfigDirectory_PHF = struct.unpack('<I', self.binary.read(4))[0]
            else:
                #  winx86
                self.LoadConfigDirectory_DFBT = struct.unpack('<I', self.binary.read(4))[0]
                self.LoadConfigDirectory_DTFT = struct.unpack('<I', self.binary.read(4))[0]
                self.LoadConfigDirectory_LPTV = struct.unpack('<I', self.binary.read(4))[0]
                self.LoadConfigDirectory_MAS = struct.unpack('<I', self.binary.read(4))[0]
                self.LoadConfigDirectory_VMT = struct.unpack('<I', self.binary.read(4))[0]
                self.LoadConfigDirectory_PHF = struct.unpack('<I', self.binary.read(4))[0]
                self.LoadConfigDirectory_PAM = struct.unpack('<I', self.binary.read(4))[0]

            self.LoadConfigDirectory_CSDV = struct.unpack('<H', self.binary.read(2))[0]
            self.LoadConfigDirectory_Reserved = struct.unpack('<H', self.binary.read(2))[0]

            if self.Magic == 0x20B:
                self.LoadConfigDirectory_ELVA = struct.unpack('<Q', self.binary.read(8))[0]
                self.LoadConfigDirectory_SCVA = struct.unpack('<Q', self.binary.read(8))[0]
                self.LoadConfigDirectory_SEHTVA = struct.unpack('<Q', self.binary.read(8))[0]
                self.LoadConfigDirectory_SEHC = struct.unpack('<Q', self.binary.read(8))[0]

            else:
                self.LoadConfigDirectory_ELVA = struct.unpack('<I', self.binary.read(4))[0]
                self.LoadConfigDirectory_SCVA = struct.unpack('<I', self.binary.read(4))[0]
                self.LoadConfigDirectory_SEHTVA = struct.unpack('<I', self.binary.read(4))[0]
                self.LoadConfigDirectory_SEHC = struct.unpack('<I', self.binary.read(4))[0]

            if self.Magic == 0x20B and self.LoadConfigDirectory_Size > 0x70:
                self.LCD_CFG_address_CF_PTR_LOC = self.binary.tell()
                self.LCD_CFG_address_CF_PTR = struct.unpack('<Q', self.binary.read(8))[0]
                self.LCD_CFG_dispatch_fptr = struct.unpack('<Q', self.binary.read(8))[0]
                self.LCD_CFG_Func_Table = struct.unpack('<Q', self.binary.read(8))[0]
                self.LCD_CFG_Func_Count = struct.unpack('<Q', self.binary.read(8))[0]
                # Zero out LCD_CFG_Guard_Flags to disable CFG
                self.LCD_CFG_Guard_Flags_LOC = self.binary.tell()
                self.LCD_CFG_Guard_Flags = struct.unpack('<I', self.binary.read(4))[0]
            elif self.Magic == 0x10b and self.LoadConfigDirectory_Size > 0x48:
                self.LCD_CFG_address_CF_PTR_LOC = self.binary.tell()
                self.LCD_CFG_address_CF_PTR = struct.unpack('<I', self.binary.read(4))[0]
                self.LCD_CFG_dispatch_fptr = struct.unpack('<I', self.binary.read(4))[0]
                self.LCD_CFG_Func_Table = struct.unpack('<I', self.binary.read(4))[0]
                self.LCD_CFG_Func_Count = struct.unpack('<I', self.binary.read(4))[0]
                # Zero out LCD_CFG_Guard_Flags to disable CFG
                self.LCD_CFG_Guard_Flags_LOC = self.binary.tell()
                self.LCD_CFG_Guard_Flags = struct.unpack('<I', self.binary.read(4))[0]

            #  Find CFG_PTR_LOC
            if hasattr(self, "LCD_CFG_dispatch_fptr"):
                if self.LCD_CFG_dispatch_fptr != 0:
                    self.LCD_CFG_dispatch_fptr_LOC = self.LCD_CFG_dispatch_fptr - self.ImageBase + self.LoadConfigTable_OFFSET
                    logger.debug(f'LCD_CFG_dispatch_fptr_LOC: {hex(self.LCD_CFG_dispatch_fptr_LOC)}')
                    self.binary.seek(self.LCD_CFG_dispatch_fptr_LOC, 0)
                    try:
                        if self.Magic == 0x20B:
                            self.CFG_text_LOC = struct.unpack('<Q', self.binary.read(8))[0] 
                        else:
                            self.CFG_text_LOC = struct.unpack('<I', self.binary.read(4))[0]

                        # self.guard_dispatch_icall_nop_LOC = self.CFG_text_LOC - self.ImageBase - 0xC00
                    except:
                        self.CFG_text_LOC = 0

        # Walk exports

        '''
        ===Export Directory Table===
        size field
        4 Export Flags 
        4 Time/Date Stamp 
        2 Major Version             
        2 Minor Version 
        4 Name RVA
        4 Ordinal Base 
        4 Address Table Entries 
        4 Number of Name Pointers 
        4 Export Address Table RVA 
        4 Name Pointer RVA 
        4 Ordinal Table RVA 
        ===END===

        '''
        if self.IsDLL:
            self.binary.seek(0)
            if self.LoadConfigTablePresent:
                self.binary.seek(self.ExportDirectoryTableRVA - abs(self.LoadConfigTable_OFFSET), 0)
                self.ExportDirectoryTableLOC = self.binary.tell()

            self.ExportFlags = struct.unpack("<I", self.binary.read(4))[0]
            self.TimeDateStamp = struct.unpack("<I", self.binary.read(4))[0]
            self.MajorVersion = struct.unpack("<H", self.binary.read(2))[0]
            self.MinorVersion = struct.unpack("<H", self.binary.read(2))[0]
            self.NameRVA = struct.unpack("<I", self.binary.read(4))[0]
            self.OrdinalBase = struct.unpack("<I", self.binary.read(4))[0]
            self.AddressTableEntries = struct.unpack("<I", self.binary.read(4))[0]
            self.NumberOfNamePointers = struct.unpack("<I", self.binary.read(4))[0]
            self.ExportAddressTableRVA = struct.unpack("<I", self.binary.read(4))[0]
            self.NamePointerRVA = struct.unpack("<I", self.binary.read(4))[0]
            self.OrdinalTableRVA = struct.unpack("<I", self.binary.read(4))[0]

            self.binary.seek(self.ExportAddressTableRVA - abs(self.LoadConfigTable_OFFSET), 0)
            self.ExportAddressTableLOC = self.binary.tell()
            # Find the section where the EAT lives
            self.EATRVAoffset = 0

            for section in self.Sections:
                # EATLOC > section Raw Location and <= section Rawlocation + section Raw Size
                if self.ExportAddressTableLOC >= section[4] and self.ExportAddressTableLOC <= section[3] + section[4]:
                    logger.debug(f"Export Table is in: {section[0]}")
                    # Export EATRVA offset == section RVA - pointer to raw data
                    self.EATRVAoffset = section[2] - section[4]

            # These all all aligned by ordinals, in order
            # put each on in lists then you list notation to reference.
            # for address in range(AddressTableEntries)
            self.ActualRVAOFFSEC = self.textVirtualAddress - self.textPointerToRawData

            logger.debug(f"{hex(self.EATRVAoffset)=}")
            self.ExportAddressTable = [] # [[ExportRVA, ExportRVALoC, ExportLOC]
            for i in range(0, self.AddressTableEntries*4, 4):
                ExportForwarderd = False
                self.binary.seek(self.ExportAddressTableLOC + i, 0)
                ExportRVALoC = self.binary.tell()
                ExportRVA = struct.unpack("<I", self.binary.read(4))[0]
                # update this to the section RVA offset it's owning section
                ExportLOC = abs(ExportRVA  - self.ActualRVAOFFSEC)

                if ExportRVA >= self.ExportDirectoryTableRVA and ExportRVA <= self.ExportDirectoryTableRVA + self.ExportTableSize:
                    ExportForwarderd = True
                else:
                    ExportForwarderd = False

                if ExportLOC < 0:
                    logger.info(f'Negative ExportLOC skipping: {hex(ExportRVA)=}, {hex(ExportRVALoC)=}, {hex(ExportLOC)=}')
                    continue   
                self.ExportAddressTable.append([[ExportRVA, ExportRVALoC, ExportLOC, ExportForwarderd]])
                logger.debug(f'{hex(ExportRVA)=}')
                if ExportRVA  != 0:
                    logger.debug(f'\t{hex(ExportLOC)=}')
                    self.binary.seek(ExportLOC - 5, 0)
                    #print("Pattern to patch (cc's are good)", hex(struct.unpack("<IB", self.binary.read(5))[0]))
            logger.debug(f"{self.ExportAddressTable=}")
            #print("="*50, "End of export addrs")

            self.EndofNameTableRVA = self.NamePointerRVA + (4 * self.NumberOfNamePointers)
            self.binary.seek(self.NamePointerRVA- abs(self.LoadConfigTable_OFFSET), 0)

            self.beginningOfNameTableLOC = self.binary.tell()
            self.ExportNames = [] # [Address, Name]
            logger.debug(f'{self.NumberOfNamePointers=}')
            for i in range(0, 4 * self.NumberOfNamePointers, 4):
                logger.debug(f'Ordinal: {(i/4)+1}')
                logger.debug(f'\tbeginningOfNameTableLOC: {hex(self.beginningOfNameTableLOC + i)}')
                self.binary.seek(self.beginningOfNameTableLOC + i, 0)
                NamePointer = struct.unpack("<I", self.binary.read(4))[0]
                logger.debug(f"NamePointer: {hex(NamePointer)}")
                NamePointerLoC = NamePointer - self.EATRVAoffset
                logger.debug(f"NamePointerLoC: {hex(NamePointerLoC)}")
                try:
                    self.binary.seek(NamePointerLoC, 0)
                except Exception as e:
                    logger.debug('Exports name pointers are jacked')
                    continue
                ExportName = self.binary.read(255).split(b"\x00")[0]
                logger.debug(f"ExportName: {ExportName}")
                self.ExportNames.append([NamePointer, NamePointerLoC, ExportName])
                logger.debug("="*50)
            logger.debug(f"{self.ExportNames=}")
        return True


if __name__ == "__main__":
    import sys
    moo = pe_parse(FILE=sys.argv[1])
