from mitmproxy import http
from mitmproxy import ctx
from mitmproxy.script import concurrent
import io
from pebin import pebin
from elfbin import elfbin
from machobin import machobin
from pe import pe_parse
from elf import elf_parse
from macho import macho_parse
from mitmproxy import ctx, command
import logging

logger = logging.getLogger(__name__)


'''
@command.command('all.markers')
def all_markers():
    'Create a new flow showing all marker values'
    for marker in emoji.emoji:
        ctx.master.commands.call('view.flows.create', 'get', f'https://example.com/{marker}')
        ctx.master.commands.call('flow.mark', [ctx.master.view.focus.flow], marker)
'''

class executable_handler():

    def __init__(self, executable, bin_type, config):
        self.executable = executable
        self.bin_type = bin_type
        self.config = config
        self.result = None
    
    def get_macho_type(self):
        self.parser = macho_parse.macho_parse(FILE=self.b_executable)

        if self.parser.run():
            self.macho_object = self.parser.__dict__
        else:
            logger.error("Mach-o Parser Failure! Wake the DEV! OK don't, but issue a bug.")
            return False

    def get_elf_type(self):
        self.parser = elf_parse.elf_parse(FILE=self.b_executable)
        logger.info('parsing elf')
        if self.parser.run():
            self.elf_object = self.parser.__dict__
        else:
            logger.error("ELF parser error, returning unmodified file")
        
    def get_pe_type(self):
        self.parser = pe_parse.pe_parse(FILE=self.b_executable, 
                                      DISK_OFFSET=0
                                      )

        if self.parser.run():
            self.pe_object = self.parser.__dict__
        else:
            logger.error("PE Parser Failure! Wake the DEV! OK don't, but issue a bug.")
            return False
   
    def set_vars(self, exe_type, dict_vars={}):
        for key, value in dict_vars.items():
            logger.debug(f"set_vars: {key}, {value}")
            self.config[exe_type][key] = value
            logger.debug(f"result: {self.config[exe_type][key]}")
        self.config[exe_type]['FILE'] = None
        self.config[exe_type]['b_FILE'] = self.b_executable

    def patch_executable(self):
        self.b_executable = io.BytesIO(self.executable[:])
        
        if self.bin_type == 'pe':
            self.get_pe_type()
            
            if self.pe_object['MachineType'] == 0x8664:
                #x64
                logger.info('Patch PE x64')
                
                self.set_vars('WindowsIntelx64')
                logger.info(self.config['WindowsIntelx64'])
                self.result = pebin(self.config['WindowsIntelx64'])
            
            elif self.pe_object['MachineType'] ==  0x14c:
                #x86
                logger.info('Patch PE x86')
                self.set_vars('WindowsIntelx86')
                self.result = pebin(self.config['WindowsIntelx86'])
                # check for x86/x64
            else:
                logger.warn('This format is unsupported')
                return None
            #self.executable = pebin(self.config.pe.x86)
            
        elif self.bin_type == 'elf':
            self.get_elf_type()
                
            if self.elf_object['e_type'] == 0x03:
                #ET_DYN : remove when supported
                logger.warn('ET_DYN binary not supported yet')
                return None
            if self.elf_object['e_machine'] == 0x03:  # x86 chipset
                if self.elf_object['EI_CLASS'] == 0x1:
                    
                    if self.elf_object['EI_OSABI'] in [0x00, 0x03]:
                        
                        self.set_vars('LinuxIntelx86')
                        self.result = elfbin(self.config['LinuxIntelx86'])

                    elif self.elf_object['EI_OSABI'] == 0x09 or self.elf_object['EI_OSABI'] == 0x0C:
                        self.set_vars('FreeBSDx86')
                        self.result = elfbin(self.config['FreeBSDx86'])
                
                        

            elif self.elf_object['e_machine'] == 0x3E:  # x86-64 chipset
                if self.elf_object['EI_CLASS'] == 0x2:
                    if self.elf_object['EI_OSABI'] in [0x00, 0x03]:
                        self.set_vars('LinuxIntelx64')
                        self.result = elfbin(self.config['LinuxIntelx64'])

                        
            elif self.elf_object['e_machine'] == 0x28:  # ARM chipsetf
                if self.elf_object['EI_CLASS'] == 0x1:
                    if self.elf_object['EI_OSABI'] == 0x00:
                        self.set_vars('LinuxArmv7_32')
                        self.result = elbin(self.config['LinuxArmv7_32'])
                        
            else:
                logger.warn('ELF format not supported')
                return None
                
            
        elif self.bin_type in ['fatfile', 'machox64', 'machox86']:
            self.get_macho_type()
            
            if self.bin_type == 'fatfile':
                if self.config['targetConfigALL']['FatPriority'].lower() == 'x86':
                    self.set_vars('MachoIntelx86',
                        {'FAT_PRIORITY' : self.config['targetConfigALL']['FatPriority']})
                    
                    self.result = machobin(self.config['MachoIntelx86'])
                
                elif self.config['targetConfigALL']['FatPriority'].lower() == 'x64':
                    self.set_vars('MachoIntelx64',
                        {'FAT_PRIORITY' : self.config['targetConfigALL']['FatPriority']})
                    
                    self.result = machobin(self.config['MachoIntelx64'])
            
            elif self.bin_type == 'machox64':
                self.set_vars('MachoIntelx64',
                    {'FAT_PRIORITY' : self.config['targetConfigALL']['FatPriority']})
                
                self.result = machobin(self.config['MachoIntelx64'])

            elif self.bin_type == 'machox86':
                self.set_vars('MachoIntelx86',
                    {'FAT_PRIORITY' : self.config['targetConfigALL']['FatPriority']})
                
                    
                self.result = machobin(self.config['MachoIntelx86'])
            
            else:
                logger.warn('This format is unsupported')
        
    def run(self):
        logger.info('In executable_handler')
        # look at self.content here and determine whether to patch or not.
        self.patch_executable()
        if self.result:
            if self.result.result:
                logger.info('Patching successful!')
                self.executable = self.result.result.read()
            else:
                logger.error(f'Patching failed, {self.result.result}')
        else:
            logger.error(f'Patching failed')
        # the default is to not modify content    
        return self.executable
    

