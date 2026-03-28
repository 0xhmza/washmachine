'''

Copyright (c) 2013-2020, Joshua Pitts

All rights reserved.

AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE
LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR
CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN
CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
POSSIBILITY OF SUCH DAMAGE.

'''


import struct
import random
from binascii import unhexlify
from capstone import *
import logging
import os
logger = logging.getLogger(__name__)



class intelCore():
    nops = [0x90, 0x3690, 0x6490, 0x6590, 0x6690, 0x6790]
        
    def __init__(self, TESTING=False):
        logger.debug("In file {0}".format(str(os.path.abspath(__file__))))
        self.TESTING = TESTING
        self.jmp_symbols = [b'jns', b'jle', b'jg', b'jp', b'jge', b'js', b'jl', b'jbe', b'jo',
                   b'jne', b'jrcxz', b'je', b'jae', b'jno', b'ja', b'jb', b'jnp', b'jmp'
                   ]
        self.name = """intelCore"""
        self.description = """Generic intel chipset support functions"""
        self.requirements = {'FILE':'Binary to be patched',
                            }

    def opcode_return(self, OpCode, instr_length):
        _, OpCode = hex(OpCode).split('0x')
        OpCode = unhexlify(OpCode)
        return OpCode

    def ones_compliment(self, TESTING=False):
        """
        Function for finding two random 4 byte numbers that make
        a 'ones compliment'
        """
        
        # REMOVE AFTER TESTING
        if TESTING:

            compliment_you = 255 # for testing
        else:
            compliment_you = random.randint(1, 4228250625)

        compliment_me = int('0xFFFFFFFF', 16) - compliment_you
        
        logger.debug(f"First ones compliment: {hex(compliment_you)}")
        logger.debug(f"2nd ones compliment: {hex(compliment_me)}")
        logger.debug("'AND' the compliments {0}: ".format(compliment_you & compliment_me))
        self.compliment_you = struct.pack('<I', compliment_you)
        self.compliment_me = struct.pack('<I', compliment_me)
   



