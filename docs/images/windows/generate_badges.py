"""Regenerate the three static README badges; run from any directory."""
from pathlib import Path

OUT = Path(__file__).parent
BADGES = [
    ("platform", "PLATFORM", "Windows x64", 174, 80, "#326eae"),
    ("runtime", "RUNTIME", ".NET 8 / WPF", 186, 78, "#6b50a1"),
    ("license", "LICENSE", "MIT", 126, 76, "#23715b"),
]

for name, label, value, width, cut, color in BADGES:
    svg = f'''<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="28" viewBox="0 0 {width} 28" role="img" aria-labelledby="title"><title id="title">{label}: {value}</title><clipPath id="round"><rect width="{width}" height="28" rx="6"/></clipPath><g clip-path="url(#round)"><rect width="{width}" height="28" fill="#293443"/><rect x="{cut}" width="{width-cut}" height="28" fill="{color}"/></g><g fill="#fff" text-anchor="middle" font-family="Segoe UI,Arial,sans-serif" font-size="11"><text x="{cut/2}" y="18">{label}</text><text x="{cut+(width-cut)/2}" y="18">{value}</text></g></svg>'''
    (OUT / f"{name}.svg").write_text(svg + "\n", encoding="utf-8")
