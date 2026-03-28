import os
import logging

logger = logging.getLogger(__name__)

try:
    from cStringIO import StringIO
except:
    from io import BytesIO as StringIO


class replace:

    def __init__(self):
        self.name = """replace"""
        self.description = """A simple binary swap."""
        self.requirements = {'FILE':'FILE to replace',
                              'SUPPLIED_BINARY': 'Binary to replace the provided binary',
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
        
        logger.info("Replacing with the following binary: %s" % self.SUPPLIED_BINARY)
        # always return a StringIO object
        result = StringIO()
        result.write(open(self.SUPPLIED_BINARY, 'rb').read())
        result.seek(0)

        return result

