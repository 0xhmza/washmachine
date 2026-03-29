from typing import List, Dict
import unittest
import pefile

from model.defs import *
from pe.superpe import SuperPe, PeSection


class SuperPeTest(unittest.TestCase):

    def test_exe(self):
        dll_filepath = PATH_INJECTABLES + "procexp64.exe"
        superpe = SuperPe(dll_filepath)

        # Properties
        self.assertFalse(superpe.is_dll())
        self.assertTrue(superpe.is_64()) 
        self.assertFalse(superpe.is_dotnet())
        self.assertEqual(superpe.get_entrypoint(), 0xE1D78)
        self.assertIsNone(superpe.get_rwx_section())

        self.assertEqual(superpe.get_image_base(), 0x140000000)
        self.assertEqual(superpe.is_dynamic_base(), True)

        # Text Section 1 (pefile SectionStructure)
        code_sect: pefile.SectionStructure = superpe.get_code_section()
        self.assertEqual(code_sect.Name.decode(), ".text\x00\x00\x00")
        self.assertEqual(code_sect.VirtualAddress, 0x1000)
        self.assertEqual(code_sect.Misc_VirtualSize, 0x11B0CE)

        # Text Section 2 (PeSection)
        code_pesect = superpe.get_section_by_name(".text")
        self.assertIsNotNone(code_pesect)
        if code_pesect is not None:
            self.assertEqual(code_pesect.name, ".text")
            self.assertEqual(code_pesect.virt_addr, 0x1000)
            self.assertEqual(code_pesect.virt_size, 0x11B0CE)

        # Relocations
        base_relocs: List[PeRelocEntry] = superpe.get_base_relocs()
        self.assertEqual(len(base_relocs), 2864)
        base_reloc = base_relocs[0]
        self.assertEqual(base_reloc.rva, 0x11E618)
        self.assertEqual(base_reloc.base_rva, 0x11E000)
        self.assertEqual(base_reloc.offset, 0x618)

        # IAT
        iat_entries: Dict[str, List[IatEntry]] = superpe.get_iat_entries()
        self.assertEqual(len(iat_entries), 24)
        self.assertTrue("kernel32.dll" in iat_entries)
        self.assertTrue("uxtheme.dll" in iat_entries)
        kernel32_entries = iat_entries["kernel32.dll"]
        self.assertEqual(len(kernel32_entries), 218)
        entry = kernel32_entries[0]
        self.assertEqual(entry.dll_name, "kernel32.dll")
        self.assertEqual(entry.func_name, "FileTimeToLocalFileTime")
        self.assertEqual(entry.iat_vaddr, 0x14011D528)

        self.assertEqual(superpe.get_vaddr_of_iatentry("FileTimeToLocalFileTime"), 0x14011D528)
        self.assertNotEqual(superpe.get_replacement_iat_for(
            "kernel32.dll", "GetEnvironmentStringsW"), "GetEnvironmentStringsW")

        # Exports
        exports = superpe.get_exports_full()
        self.assertEqual(len(exports), 0)

        # VRA/Virt to Phys/Raw
        #raw = superpe.get_offset_from_rva(0xD690)
        #self.assertEqual(raw, 0xCA90)


    def test_dll(self):
        dll_filepath = PATH_INJECTABLES + "TestDLL.dll"
        superpe = SuperPe(dll_filepath)

        # Properties
        self.assertTrue(superpe.is_dll())
        self.assertTrue(superpe.is_64())
        self.assertFalse(superpe.is_dotnet())
        self.assertEqual(superpe.get_entrypoint(), 0x13B0)
        self.assertIsNone(superpe.get_rwx_section())

        self.assertEqual(superpe.get_image_base(), 0x180000000)
        self.assertEqual(superpe.is_dynamic_base(), True)

        # Text Section 1 (pefile SectionStructure)
        code_sect: pefile.SectionStructure = superpe.get_code_section()
        self.assertEqual(code_sect.Name.decode(), ".text\x00\x00\x00")
        self.assertEqual(code_sect.VirtualAddress, 0x1000)
        self.assertEqual(code_sect.Misc_VirtualSize, 3912)

        # Text Section 2 (PeSection)
        code_pesect: PeSection|None = superpe.get_section_by_name(".text")
        self.assertIsNotNone(code_pesect)
        if code_pesect is not None:
            self.assertEqual(code_pesect.name, ".text")
            self.assertEqual(code_pesect.virt_addr, 0x1000)
            self.assertEqual(code_pesect.virt_size, 3912)

        # Relocations
        base_relocs: List[PeRelocEntry] = superpe.get_base_relocs()
        self.assertEqual(len(base_relocs), 17)
        base_reloc = base_relocs[0]
        self.assertEqual(base_reloc.rva, 0x20F8)
        self.assertEqual(base_reloc.base_rva, 0x2000)
        self.assertEqual(base_reloc.offset, 0xF8)

        # IAT
        iat_entries: Dict[str, List[IatEntry]] = superpe.get_iat_entries()
        self.assertEqual(len(iat_entries), 4)
        self.assertTrue("kernel32.dll" in iat_entries)
        self.assertTrue("vcruntime140.dll" in iat_entries)
        
        kernel32_entries = iat_entries["kernel32.dll"]
        self.assertEqual(len(kernel32_entries), 14)
        entry = kernel32_entries[0]
        self.assertEqual(entry.dll_name, "kernel32.dll")
        self.assertEqual(entry.func_name, "GetSystemTimeAsFileTime")
        self.assertEqual(entry.iat_vaddr, 0x180002000)

        self.assertIsNone(superpe.get_vaddr_of_iatentry("asdf"))
        self.assertEqual(superpe.get_vaddr_of_iatentry("RtlCaptureContext"), 0x180002008)
        
        # bad test, but result is random
        self.assertNotEqual(superpe.get_replacement_iat_for(
            "kernel32.dll", "GetEnvironmentStringsW"), "GetEnvironmentStringsW")

        # Exports
        exports = superpe.get_exports_full()
        self.assertEqual(len(exports), 6)
        export = exports[0]
        self.assertEqual(export["name"], "test")
        self.assertEqual(export["addr"], 0x1000)
        self.assertEqual(export["size"], 80)

        # VRA/Virt to Phys/Raw
        raw = superpe.get_offset_from_rva(0x1000)  # test export
        self.assertEqual(raw, 0x400)
