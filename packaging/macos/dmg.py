# Settings for dmgbuild (https://dmgbuild.readthedocs.io), which writes the window layout without needing Finder.
# Run from the repo root: dmgbuild -s packaging/macos/dmg.py -D app=<path to Shirabe.app> Shirabe <out.dmg>
files = [defines["app"]]  # noqa: F821 (dmgbuild provides "defines")
symlinks = {"Applications": "/Applications"}
format = "UDZO"
icon = "packaging/macos/Shirabe.icns"  # of the mounted volume
background = "packaging/macos/dmg-background.png"  # dmg-background@2x.png beside it is picked up for Retina
window_rect = ((200, 120), (660, 400))
icon_size = 128
text_size = 13
# The positions the background is drawn around (packaging/art/make_art.py).
icon_locations = {"Shirabe.app": (180, 190), "Applications": (480, 190)}
