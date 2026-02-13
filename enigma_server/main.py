from fastapi import FastAPI
import importlib
import pkgutil
import apis

app = FastAPI()


def load_routers(package):
    for _, module_name, _ in pkgutil.walk_packages(
        package.__path__,
        package.__name__ + "."
    ):
        module = importlib.import_module(module_name)

        if hasattr(module, "router"):
            app.include_router(module.router)


load_routers(apis)
