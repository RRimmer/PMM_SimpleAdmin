#!/usr/bin/env python3
"""
Generates the PMM_SimpleAdmin panorama: pmm_admin.xml + pmm_admin.css (design "variant A - Classic+").

    python3 gen_layout.py <addon root>      (writes <root>/panorama/...)

The plugin (Panel.cs) drives ids and class names from the constants below, keep them in sync:
  rows      sa-r-0..63           Button, one per menu option. No pages: the list scrolls with the mouse wheel
            vars  sa_lN (label)  sa_hN (hint on the right)  sa_pN (pill: ON / OFF)
            row classes  slot-off r-danger r-dim r-dis r-arrow r-hint r-team r-icon r-pill pill-on tm-ct tm-t tm-s
            icon panel   sa-ic-N  (one class ic-<name>, see ICONS)
  list      #sa-list  one class h-1..h-8 (fits the rows) or h-scroll (8.5 rows, scrolls)
  keys test sa-ck-0..9 (keys 1..9, 0), class ck-ok when the server got the key
            #sa-pfill  progress bar, one class p-0..p-10 (keys received); root class cal-done when all 10 came
  buttons   sa-back sa-next sa-close
  root      #sa-root  classes sa-hidden has-sub no-back has-next view-cal pos-left|pos-center|pos-right
                              acc-red|acc-bright|acc-dark|acc-coral  mode-scroll|mode-pages|mode-keys
            mode-scroll: wheel, no numbers;  mode-pages: pages, no numbers;  mode-keys: pages, 1-7 / 8 back / 9 next / 0 close
            vars  sa_brand sa_count sa_crumb sa_title sa_sub sa_back sa_close
Every dialog variable is set on #sa-root.
"""
import pathlib, sys

ROWS = 64          # pool: the longest SimpleAdmin list (players, maps, plugins) fits
VISIBLE = 8        # rows shown without scrolling
ROW_H = 42
LIST_PAD = 12      # padding-top 4 + padding-bottom 8

# icon class name -> game image (CS2 client VPK, panorama/images/icons/<path>.vsvg).
# Same paths as PMM_StoreCore (checked in game).
ICONS = {
    "teamplayer": "ui/teamplayer", "settings": "ui/settings", "shield": "ui/shield", "star": "ui/star",
    "key": "ui/key", "leader": "ui/leader", "shoppingcart": "ui/shoppingcart", "random": "ui/random",
    "gift": "ui/gift", "kill": "ui/kill", "clock": "ui/clock", "warning": "ui/warning", "lock": "ui/locked",
    "refresh": "ui/refresh", "trophy": "ui/trophy", "crosshair": "ui/crosshair", "health": "ui/health",
    "fast": "ui/fast", "parachute": "ui/parachute", "dollar_sign": "ui/dollar_sign", "coin_stack": "ui/coin_stack",
    "bullet": "ui/bullet", "inventory": "ui/inventory", "timer": "ui/timer", "check": "ui/check",
    "clantag": "ui/clantag", "sound_3": "ui/sound_3", "loadout": "ui/loadout", "colorwheel": "ui/colorwheel",
}


def xml() -> str:
    o = []
    o.append('<root>\n\t<styles>\n')
    o.append('\t\t<include src="s2r://panorama/styles/csgostyles.vcss_c" />\n')
    o.append('\t\t<include src="s2r://panorama/styles/custom_game/pmm_admin.vcss_c" />\n')
    o.append('\t</styles>\n')
    o.append('\t<Panel class="sa-screen" hittest="false">\n')
    o.append('\t\t<Panel id="sa-root" class="sa-root sa-hidden pos-left acc-red mode-scroll">\n')
    # header
    o.append('\t\t\t<Panel class="sa-head" hittest="false">\n')
    o.append('\t\t\t\t<Label class="sa-brand stratum-bold-tf" text="{s:sa_brand}" />\n')
    o.append('\t\t\t\t<Label class="sa-count stratum-medium-tf" text="{s:sa_count}" />\n')
    o.append('\t\t\t</Panel>\n')
    # titles
    o.append('\t\t\t<Panel class="sa-titles" hittest="false">\n')
    o.append('\t\t\t\t<Label class="sa-crumb stratum-medium-tf" text="{s:sa_crumb}" />\n')
    o.append('\t\t\t\t<Label class="sa-title stratum-bold-tf" text="{s:sa_title}" />\n')
    o.append('\t\t\t\t<Label class="sa-sub stratum-medium-tf" text="{s:sa_sub}" />\n')
    o.append('\t\t\t</Panel>\n')
    # key test (calibration of the number keys)
    o.append('\t\t\t<Panel class="sa-cal" hittest="false">\n')
    o.append('\t\t\t\t<Panel class="sa-ckrow" hittest="false">\n')
    for k in range(10):
        key = (k + 1) % 10
        o.append(f'\t\t\t\t\t<Label id="sa-ck-{k}" class="sa-ck stratum-bold-tf" text="{key}" />\n')
    o.append('\t\t\t\t</Panel>\n')
    o.append('\t\t\t\t<Panel class="sa-pbar" hittest="false"><Panel id="sa-pfill" class="sa-pfill p-0" hittest="false" /></Panel>\n')
    o.append('\t\t\t</Panel>\n')
    # rows (scroll)
    o.append('\t\t\t<Panel id="sa-list" class="sa-list h-1">\n')
    for i in range(ROWS):
        o.append(f'\t\t\t\t<Button id="sa-r-{i}" class="sa-row slot-off">\n')
        o.append('\t\t\t\t\t<Panel class="sa-rbar" hittest="false" />\n')
        o.append(f'\t\t\t\t\t<Label class="sa-key stratum-medium-tf" text="{i + 1}" />\n')
        o.append('\t\t\t\t\t<Panel class="sa-tm" hittest="false" />\n')
        o.append(f'\t\t\t\t\t<Panel id="sa-ic-{i}" class="sa-ic ic-star" hittest="false" />\n')
        o.append(f'\t\t\t\t\t<Label class="sa-lab stratum-bold-tf" text="{{s:sa_l{i}}}" />\n')
        o.append(f'\t\t\t\t\t<Label class="sa-hint stratum-medium-tf" text="{{s:sa_h{i}}}" />\n')
        o.append(f'\t\t\t\t\t<Label class="sa-pill stratum-bold-tf" text="{{s:sa_p{i}}}" />\n')
        o.append('\t\t\t\t\t<Label class="sa-arrow stratum-medium-tf" text="›" />\n')
        o.append('\t\t\t\t</Button>\n')
    o.append('\t\t\t</Panel>\n')
    # footer
    o.append('\t\t\t<Panel class="sa-foot">\n')
    o.append('\t\t\t\t<Button id="sa-back" class="sa-fb fb-back"><Label class="sa-fk stratum-medium-tf" text="8" /><Label class="sa-ft stratum-medium-tf" text="{s:sa_back}" /></Button>\n')
    o.append('\t\t\t\t<Button id="sa-next" class="sa-fb fb-next"><Label class="sa-fk stratum-medium-tf" text="9" /><Label class="sa-ft stratum-medium-tf" text="{s:sa_next}" /></Button>\n')
    o.append('\t\t\t\t<Button id="sa-close" class="sa-fb fb-close"><Label class="sa-fk stratum-medium-tf" text="0" /><Label class="sa-ft stratum-medium-tf" text="{s:sa_close}" /></Button>\n')
    o.append('\t\t\t</Panel>\n')
    o.append('\t\t</Panel>\n')
    o.append('\t</Panel>\n</root>\n')
    return "".join(o)


CSS_HEAD = """/* ============================================================
   PMM_SimpleAdmin - admin panel (generated by tools/gen_layout.py)
   Server drives: dialog variables on #sa-root, classes per player.
   Root classes: sa-hidden, has-sub, no-back, has-next, view-cal, pos-left | pos-center | pos-right,
                 acc-red | acc-bright | acc-dark | acc-coral, mode-scroll | mode-pages | mode-keys
   List classes: h-1 .. h-8 | h-scroll
   Row classes:  slot-off, r-danger, r-dim, r-dis, r-arrow, r-hint, r-team, r-icon, r-pill, pill-on, tm-ct | tm-t | tm-s
   ============================================================ */

@define dur: 0.12s;

.sa-screen
{
	width: 100%;
	height: 100%;
	z-index: 2000000000;
}

.sa-root
{
	width: 460px;
	height: fit-children;
	flow-children: down;
	horizontal-align: left;
	vertical-align: center;
	margin-left: 40px;
	background-color: #160a0bf2;
	border: 1px solid #5c1a1c;
	border-radius: 6px;
	box-shadow: #000000b0 0px 14px 40px 0px;
	overflow: clip clip;
}

.sa-root.sa-hidden { visibility: collapse; }
.sa-root.pos-center { horizontal-align: center; margin-left: 0px; }
.sa-root.pos-right { horizontal-align: right; margin-left: 0px; margin-right: 40px; }

/* ---------- header ---------- */

.sa-head
{
	width: 100%;
	height: 42px;
	flow-children: right;
	padding: 0px 16px;
	background-color: #b3201f;
}

.sa-root.acc-bright .sa-head { background-color: #d7322b; }
.sa-root.acc-dark .sa-head { background-color: #8c1518; }
.sa-root.acc-coral .sa-head { background-color: #e04a3a; }

.sa-brand
{
	width: fill-parent-flow( 1.0 );
	vertical-align: center;
	font-size: 13px;
	letter-spacing: 1.8px;
	color: #ffe9e6;
	text-transform: uppercase;
	white-space: nowrap;
	text-overflow: ellipsis;
}

.sa-count
{
	vertical-align: center;
	font-size: 13px;
	color: #ffd7d2;
	white-space: nowrap;
}

/* ---------- titles ---------- */

.sa-titles
{
	width: 100%;
	height: fit-children;
	flow-children: down;
	padding: 12px 16px 8px 16px;
}

.sa-crumb
{
	width: 100%;
	font-size: 13px;
	color: #b79c99;
	white-space: nowrap;
	text-overflow: ellipsis;
}

.sa-title
{
	width: 100%;
	margin-top: 2px;
	font-size: 22px;
	color: #f4e9e7;
	white-space: nowrap;
	text-overflow: shrink;
}

.sa-sub
{
	width: 100%;
	margin-top: 3px;
	font-size: 13px;
	color: #c9a9a5;
	white-space: nowrap;
	text-overflow: ellipsis;
	visibility: collapse;
}

.sa-root.has-sub .sa-sub { visibility: visible; }

/* ---------- number keys test ---------- */

.sa-cal
{
	width: 100%;
	height: fit-children;
	flow-children: down;
	padding: 4px 12px 10px 12px;
	visibility: collapse;
}

.sa-ckrow
{
	width: 100%;
	height: 40px;
	flow-children: right;
}

.sa-root.view-cal .sa-cal { visibility: visible; }

.sa-ck
{
	width: 38px;
	height: 38px;
	margin-right: 5px;
	border-radius: 4px;
	border: 1px solid #7a2023;
	background-color: #2a0f11;
	font-size: 18px;
	color: #ff8a7e;
	text-align: center;
	vertical-align: center;
	padding-top: 7px;
	transition-property: background-color;
	transition-duration: dur;
	transition-timing-function: ease-out;
}

.sa-ck:last-child { margin-right: 0px; }
.sa-ck.ck-ok { background-color: #3d5a1f; border: 1px solid #8fc14a; color: #e4f7c8; }

/* progress bar: a clip ladder, not width (width restarts its transition when a label changes) */
.sa-pbar
{
	width: 100%;
	height: 8px;
	margin-top: 8px;
	border-radius: 4px;
	background-color: #2a0f11;
	border: 1px solid #4a1416;
}

.sa-pfill
{
	width: 100%;
	height: 100%;
	border-radius: 4px;
	background-color: #ff4a3d;
	clip: rect( 0%, 0%, 100%, 0% );
	transition-property: clip, background-color;
	transition-duration: 0.25s;
	transition-timing-function: ease-out;
}

.sa-root.cal-done .sa-pfill { background-color: #8fc14a; }

/* ---------- rows: one list, scrolls with the mouse wheel ---------- */

.sa-list
{
	width: 100%;
	flow-children: down;
	padding: 4px 8px 8px 8px;
	overflow: clip scroll;
}

/* room for the scroll bar: the hints on the right must not go under it */
.sa-list.h-scroll { padding-right: 20px; }

"""

CSS_ROWS = """
.sa-row
{
	width: 100%;
	height: 42px;
	flow-children: right;
	border-radius: 4px;
	background-color: #00000000;
	transition-property: background-color;
	transition-duration: dur;
	transition-timing-function: ease-out;
}

.sa-row:hover { background-color: #3a1012; }
.sa-row.r-dis:hover { background-color: #00000000; }
.sa-row.slot-off { visibility: collapse; }

.sa-rbar
{
	width: 3px;
	height: 100%;
	border-radius: 2px;
	background-color: #00000000;
}

.sa-row:hover .sa-rbar { background-color: #ff4a3d; }
.sa-row.r-dis:hover .sa-rbar { background-color: #00000000; }

.sa-key
{
	width: 28px;
	vertical-align: center;
	margin-left: 7px;
	font-size: 15px;
	color: #ff6b5e;
}

.sa-tm
{
	width: 4px;
	height: 20px;
	vertical-align: center;
	margin-right: 10px;
	border-radius: 2px;
	background-color: #6e6764;
	visibility: collapse;
}

.sa-root.mode-scroll .sa-key, .sa-root.mode-pages .sa-key { visibility: collapse; }
.sa-root.mode-scroll .sa-rbar, .sa-root.mode-pages .sa-rbar { margin-right: 13px; }

.sa-row.r-team .sa-tm { visibility: visible; }
.sa-row.tm-ct .sa-tm { background-color: #5b8def; }
.sa-row.tm-t .sa-tm { background-color: #e8a33d; }
.sa-row.tm-s .sa-tm { background-color: #6e6764; }

.sa-ic
{
	width: 20px;
	height: 20px;
	vertical-align: center;
	margin-right: 10px;
	background-size: 100% 100%;
	wash-color: #ff6b5e;
	visibility: collapse;
}

.sa-row.r-icon .sa-ic { visibility: visible; }

.sa-lab
{
	width: fill-parent-flow( 1.0 );
	vertical-align: center;
	font-size: 17px;
	color: #f4e9e7;
	white-space: nowrap;
	text-overflow: ellipsis;
}

.sa-row.r-danger .sa-lab { color: #ff8a7e; }
.sa-row.r-dim .sa-lab, .sa-row.r-dis .sa-lab { color: #7f6a68; }

.sa-hint
{
	vertical-align: center;
	margin-left: 10px;
	font-size: 13px;
	color: #9c8582;
	white-space: nowrap;
	visibility: collapse;
}

.sa-row.r-hint .sa-hint { visibility: visible; }

.sa-pill
{
	vertical-align: center;
	margin-left: 10px;
	margin-right: 8px;
	padding: 3px 8px;
	border-radius: 3px;
	font-size: 11px;
	letter-spacing: 0.8px;
	color: #9c8582;
	background-color: #2a1a1b;
	white-space: nowrap;
	visibility: collapse;
}

.sa-row.r-pill .sa-pill { visibility: visible; }
.sa-row.pill-on .sa-pill { color: #b7e07c; background-color: #2c3a1e; }

.sa-arrow
{
	width: 20px;
	vertical-align: center;
	margin-left: 6px;
	margin-right: 6px;
	font-size: 20px;
	color: #7a5c59;
	text-align: right;
	visibility: collapse;
}

.sa-row.r-arrow .sa-arrow { visibility: visible; }
.sa-row:hover .sa-arrow { color: #ff6b5e; }

/* ---------- footer ---------- */

.sa-foot
{
	width: 100%;
	height: 44px;
	flow-children: right;
	padding: 0px 8px;
	border-top: 1px solid #3a1214;
}

.sa-fb
{
	height: 32px;
	vertical-align: center;
	flow-children: right;
	padding: 0px 10px;
	margin-right: 6px;
	border-radius: 4px;
	background-color: #00000000;
	transition-property: background-color;
	transition-duration: dur;
	transition-timing-function: ease-out;
}

.sa-fb:hover { background-color: #3a1012; }
.sa-fb.fb-close { horizontal-align: right; margin-right: 0px; }
.sa-root.no-back .fb-back { visibility: collapse; }
.sa-fb.fb-next { visibility: collapse; }
.sa-root.has-next .fb-next { visibility: visible; }

.sa-fk
{
	vertical-align: center;
	font-size: 16px;
	color: #ff6b5e;
}

.sa-root.mode-scroll .sa-fk, .sa-root.mode-pages .sa-fk { visibility: collapse; }
.sa-root.mode-scroll .sa-ft, .sa-root.mode-pages .sa-ft { margin-left: 0px; }

.sa-ft
{
	vertical-align: center;
	margin-left: 6px;
	font-size: 14px;
	color: #c9b3b0;
	white-space: nowrap;
}

.sa-fb:hover .sa-ft { color: #ffffff; }

/* ---------- icons ---------- */

"""


def css() -> str:
    o = [CSS_HEAD]
    # list height: fits 1..VISIBLE rows exactly, longer lists get VISIBLE + half a row and scroll
    for n in range(11):
        o.append(f'.sa-pfill.p-{n} {{ clip: rect( 0%, {n * 10}%, 100%, 0% ); }}\n')
    for n in range(1, VISIBLE + 1):
        o.append(f'.sa-list.h-{n} {{ height: {n * ROW_H + LIST_PAD}px; }}\n')
    o.append(f'.sa-list.h-scroll {{ height: {VISIBLE * ROW_H + ROW_H // 2 + LIST_PAD}px; }}\n')
    o.append(CSS_ROWS)
    for name, path in ICONS.items():
        o.append(f'.ic-{name} {{ background-image: url("s2r://panorama/images/icons/{path}.vsvg"); }}\n')
    return "".join(o)


def main():
    root = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else ".")
    lay = root / "panorama" / "layout" / "custom_game"
    sty = root / "panorama" / "styles" / "custom_game"
    lay.mkdir(parents=True, exist_ok=True)
    sty.mkdir(parents=True, exist_ok=True)
    (lay / "pmm_admin.xml").write_text(xml(), encoding="utf-8", newline="\n")
    (sty / "pmm_admin.css").write_text(css(), encoding="utf-8", newline="\n")
    print("written:", lay / "pmm_admin.xml", sty / "pmm_admin.css")
    print("rows:", ROWS, "visible:", VISIBLE)
    print("icons:", " ".join(ICONS))


if __name__ == "__main__":
    main()
