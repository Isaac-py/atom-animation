import os
import shutil

# Get the folder where the script lives
script_dir = os.path.dirname(__file__)

source_folder = os.path.join(script_dir, "xyz_frames")
dest_folder = os.path.join(script_dir, "txt_frames")

os.makedirs(dest_folder, exist_ok=True)

for filename in os.listdir(source_folder):
    if filename.endswith(".xyz"):
        base = os.path.splitext(filename)[0]
        src = os.path.join(source_folder, filename)
        dst = os.path.join(dest_folder, base + ".txt")
        shutil.copy(src, dst)
        print(f"✅ {filename} → {base}.txt")

print("🎉 All .xyz files copied and renamed to .txt in:", dest_folder)