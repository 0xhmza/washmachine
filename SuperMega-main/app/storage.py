import pickle
import os
import pickle
import logging

from typing import List, Tuple
from model.defs import *
from model.project import Settings

logger = logging.getLogger("Storage")


class Storage():
    def __init__(self):
        pass


    def get_project_settings(self) -> List[Settings]:
        project_settings: List[Settings] = []
        for project_name in os.listdir(PATH_WEB_PROJECT):
            project_setting = self.get_project_setting(project_name)
            if project_setting is None:
                continue
            project_settings.append(project_setting)
        return project_settings
    

    def get_project_setting(self, project_name: str) -> Settings| None:
        path = "{}/{}".format(PATH_WEB_PROJECT, project_name)
        json_path = "{}/project.pickle".format(path)
        if not os.path.exists(json_path):
            return None
        logger.info("Loading project from: {}".format(json_path))
        with open(json_path, "rb") as f:
            settings = pickle.load(f)
        return settings


    def add_project_setting(self, settings: Settings):
        os.makedirs(PATH_WEB_PROJECT + settings.project_name, exist_ok=True)
        with open("{}/{}/project.pickle".format(PATH_WEB_PROJECT, settings.project_name), "wb") as f:
            pickle.dump(settings, f)


    def save_project_settings(self, settings: Settings):
        with open("{}/{}/project.pickle".format(PATH_WEB_PROJECT, settings.project_name), "wb") as f:
            pickle.dump(settings, f)


storage = Storage()
