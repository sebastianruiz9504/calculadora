"""Add Otras actividades to the existing Copiers activity choice; read-only by default."""
import argparse
import json
from provision_copiers_counter_activity import run


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()
    print(json.dumps(run(args.apply, value=827270013), ensure_ascii=True, indent=2))
