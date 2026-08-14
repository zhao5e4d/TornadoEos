"""Generate a DRAFT SPT limited reverse-engineering authorization PDF.

Uses real, measured camera/project data from this workspace. The PDF is NOT a
granted license and must not be presented as signed by SPT.
"""

from __future__ import annotations

from datetime import date
from pathlib import Path

from fpdf import FPDF

# --- Real data available from this project / public SPT contact page ---
ISSUE_DATE = date(2026, 8, 9)
DOC_NO = f"SPT-LRE-DRAFT-{ISSUE_DATE:%Y%m%d}-R50"

SPT_LEGAL_NAME = "Society of Photo-Technologists (SPT) / C&C Associates Technologies"
SPT_ADDRESS_LINES = [
    "11112 S. Spotted Road",
    "Cheney, WA 99004-9038",
    "USA",
]
SPT_PHONE = "509-710-4464"
SPT_WEBSITE = "https://spt.info/"
SPT_CONTACT = "https://spt.info/sptstore.php/contact/"

CAMERA_MODEL = "Canon EOS R50"
CAMERA_MARKET = "Japanese-market regional body"
FIRMWARE_DISPLAY = "1.5.0"
FIRMWARE_WPD = "3-1.5.0"
MODEL_ID = "0x80000480"
USB_VID = "0x04A9"
USB_PID = "0x330D"
USB_PROTOCOL = "MTP 1.00"
DEVICE_SERIAL = "6f63db071241f1df1d2d2bad4c755979"
DEVICE_ID = (
    r"\\?\usb#vid_04a9&pid_330d&mi_00#6&1d31853a&2&0000#"
    r"{6ac27878-a6fa-4155-ba85-f98f491d4f33}"
)
VENDOR_OPCODES = 136
EOS_PROPERTIES = 150
WPD_PROPERTIES = 23

DEMO_PACKAGE = "EOS_R6_ProADVANCED_DEMO_4_3_0.zip"
PROJECT_NAME = "TornadoEos"
REPO_PATH = r"C:\Users\86152\Projects\TornadoEos"

OUT_PATH = Path(__file__).with_name(
    "SPT-Limited-Reverse-Engineering-Authorization-DRAFT.pdf"
)


FONT_DIR = Path(r"C:\Windows\Fonts")
FONT_REG = FONT_DIR / "arial.ttf"
FONT_BOLD = FONT_DIR / "arialbd.ttf"
FONT_ITAL = FONT_DIR / "ariali.ttf"


class AuthPdf(FPDF):
    def header(self) -> None:
        self.set_font("Body", "B", 9)
        self.set_text_color(160, 40, 40)
        self.multi_cell(
            0,
            5,
            "DRAFT ONLY - NOT ISSUED OR SIGNED BY SPT - NO LEGAL FORCE UNTIL SPT EXECUTES",
            align="C",
        )
        self.ln(2)
        self.set_draw_color(160, 40, 40)
        self.line(10, self.get_y(), 200, self.get_y())
        self.ln(4)

    def footer(self) -> None:
        self.set_y(-15)
        self.set_font("Body", "I", 8)
        self.set_text_color(100, 100, 100)
        self.cell(
            0,
            8,
            f"Page {self.page_no()}/{{nb}}  |  {DOC_NO}  |  Draft generated {ISSUE_DATE.isoformat()}",
            align="C",
        )

    def h1(self, text: str) -> None:
        self.set_font("Body", "B", 14)
        self.set_text_color(20, 20, 20)
        self.multi_cell(0, 7, text)
        self.ln(2)

    def h2(self, text: str) -> None:
        self.ln(2)
        self.set_font("Body", "B", 11)
        self.set_text_color(30, 30, 30)
        self.multi_cell(0, 6, text)
        self.ln(1)

    def body(self, text: str) -> None:
        self.set_font("Body", "", 10)
        self.set_text_color(25, 25, 25)
        self.multi_cell(0, 5, text)
        self.ln(1)

    def bullet(self, text: str) -> None:
        self.set_font("Body", "", 10)
        self.set_text_color(25, 25, 25)
        x = self.get_x()
        self.cell(6, 5, "-")
        self.multi_cell(0, 5, text)
        self.set_x(x)

    def kv(self, key: str, value: str) -> None:
        left = self.l_margin
        usable = self.w - self.l_margin - self.r_margin
        key_w = 52
        self.set_xy(left, self.get_y())
        self.set_font("Body", "B", 9)
        y0 = self.get_y()
        self.multi_cell(key_w, 5, key)
        y1 = self.get_y()
        self.set_xy(left + key_w, y0)
        self.set_font("Body", "", 9)
        self.multi_cell(usable - key_w, 5, value)
        self.set_y(max(y1, self.get_y()))


def build() -> Path:
    pdf = AuthPdf(format="A4", unit="mm")
    pdf.add_font("Body", "", str(FONT_REG))
    pdf.add_font("Body", "B", str(FONT_BOLD))
    pdf.add_font("Body", "I", str(FONT_ITAL))
    pdf.alias_nb_pages()
    pdf.set_auto_page_break(auto=True, margin=18)
    pdf.set_margins(14, 18, 14)
    pdf.add_page()

    pdf.h1("LIMITED REVERSE ENGINEERING AUTHORIZATION")
    pdf.set_font("Body", "B", 11)
    pdf.multi_cell(0, 6, "(Draft for SPT execution - Applicant-prepared)")
    pdf.ln(2)

    pdf.kv("Document No.:", DOC_NO)
    pdf.kv("Prepared date:", ISSUE_DATE.strftime("%d %B %Y"))
    pdf.kv("Status:", "DRAFT - awaiting SPT authorized signature")
    pdf.kv("Proposed term:", f"{ISSUE_DATE.isoformat()} to {(ISSUE_DATE.replace(year=ISSUE_DATE.year + 1)).isoformat()}")
    pdf.ln(2)

    pdf.h2("1. Parties")
    pdf.body("Licensor (to be completed / confirmed by SPT):")
    pdf.bullet(SPT_LEGAL_NAME)
    for line in SPT_ADDRESS_LINES:
        pdf.bullet(line)
    pdf.bullet(f"Phone: {SPT_PHONE}")
    pdf.bullet(f"Website: {SPT_WEBSITE}")
    pdf.bullet(f"Contact form: {SPT_CONTACT}")
    pdf.ln(1)
    pdf.body(
        "Licensee (Applicant — personal identity fields left blank where not "
        "available in the project workspace; complete before sending to SPT):"
    )
    pdf.kv("Full legal name:", "[TO BE COMPLETED BY APPLICANT]")
    pdf.kv("Country / region:", "[TO BE COMPLETED BY APPLICANT]")
    pdf.kv("Email:", "[TO BE COMPLETED BY APPLICANT]")
    pdf.kv("Prior SPT ticket / order:", "[TO BE COMPLETED IF APPLICABLE]")
    pdf.kv("Project path:", REPO_PATH)

    pdf.h2("2. Background (actual measured data)")
    pdf.body(
        "The Applicant owns the camera identified below and operates a personal, "
        f"non-commercial interoperability project named {PROJECT_NAME}. "
        "The following identification data were obtained by a read-only "
        "Windows Portable Devices / MTP query against the Applicant's own camera "
        "(no property writes; no service-mode entry):"
    )
    pdf.kv("Camera model:", CAMERA_MODEL)
    pdf.kv("Market / language set:", f"{CAMERA_MARKET} (Japanese + English menus)")
    pdf.kv("Firmware (user-facing):", FIRMWARE_DISPLAY)
    pdf.kv("Firmware (WPD string):", FIRMWARE_WPD)
    pdf.kv("Canon model ID:", MODEL_ID)
    pdf.kv("USB identity:", f"Canon VID {USB_VID}, PID {USB_PID}")
    pdf.kv("Protocol:", USB_PROTOCOL)
    pdf.kv("Device serial (WPD):", DEVICE_SERIAL)
    pdf.kv("WPD DeviceId:", DEVICE_ID)
    pdf.kv("WPD properties read:", str(WPD_PROPERTIES))
    pdf.kv("Canon vendor opcodes exposed:", str(VENDOR_OPCODES))
    pdf.kv("EOS device properties enumerated:", str(EOS_PROPERTIES))
    pdf.ln(1)
    pdf.body(
        f"The Applicant downloaded the free demonstration package "
        f"{DEMO_PACKAGE} from the official SPT website. That archive contains "
        "R6 demonstration components and does not contain an EOS R50-specific "
        "BootFaexe.bin. The Applicant understands that model-specific service "
        "files must not be cross-used between camera models."
    )

    pdf.h2("3. Requested limited exception (scope)")
    pdf.body(
        "Notwithstanding reverse-engineering restrictions in the EULA applicable "
        f"to {DEMO_PACKAGE}, the Applicant requests that SPT grant a limited, "
        "non-exclusive, non-transferable, revocable written permission to perform "
        "ONLY the following:"
    )
    pdf.bullet(
        "Limited static analysis (including decompilation/disassembly) of the "
        "SPT demonstration components solely to identify WPD/MTP interoperability "
        "interfaces used with a supported Canon camera."
    )
    pdf.bullet(
        "Dynamic debugging and observation of USB/WPD/MTP communications while "
        "the demonstration software runs on the Applicant's own computer and "
        "communicates with the Applicant's own camera."
    )
    pdf.bullet(
        "Identification of protocol operation codes, packet structures, camera "
        "model validation, and factory-session bootstrap sequences as observable "
        "in demonstration behavior."
    )
    pdf.bullet(
        f"Independent implementation of compatible protocol behavior in "
        f"{PROJECT_NAME}, without copying SPT source code or redistributing "
        "SPT binaries."
    )
    pdf.bullet(
        "Private testing exclusively on a camera personally owned by the Applicant."
    )

    pdf.h2("4. Explicit exclusions (prohibited)")
    pdf.body("If granted, the Applicant agrees NOT to:")
    pdf.bullet("Remove or bypass SPT activation or license controls;")
    pdf.bullet("Enable or reproduce unrelated paid repair functions;")
    pdf.bullet(
        "Publish or redistribute SPT executables, libraries, license information, "
        "or proprietary service files;"
    )
    pdf.bullet(
        "Redistribute any BootFaexe.bin supplied under confidential or restricted terms;"
    )
    pdf.bullet(
        f"Represent {PROJECT_NAME} as an official or SPT-authorized product;"
    )
    pdf.bullet(
        "Use the resulting work commercially without separate written authorization."
    )

    pdf.h2("5. BootFaexe.bin")
    pdf.body(
        "This draft does not itself authorize redistribution of BootFaexe.bin. "
        "SPT may elect one of the following when executing this instrument:"
    )
    pdf.bullet("Option A — No file supplied; Applicant uses only separately authorized channels.")
    pdf.bullet(
        "Option B — Restricted supply of an EOS R50-compatible BootFaexe.bin under "
        "a confidential attachment for personal use on the identified camera only."
    )
    pdf.bullet("Option C — Referral to an authorized service partner / channel.")

    pdf.h2("6. Alternatives requested if exception is refused")
    pdf.bullet("Official WPD/MTP protocol documentation;")
    pdf.bullet("An SDK or documented interoperability interface;")
    pdf.bullet("An EOS R50-compatible service utility;")
    pdf.bullet("A legitimate EOS R50 BootFaexe.bin with usage instructions; or")
    pdf.bullet("Referral to an authorized party that can provide the required file or service.")

    pdf.h2("7. Conditions (for SPT to check if required)")
    pdf.bullet("[ ] Government-issued photo ID")
    pdf.bullet(f"[ ] Proof of ownership matching serial {DEVICE_SERIAL}")
    pdf.bullet("[ ] Separate confidentiality / limited-use agreement")
    pdf.bullet("[ ] Signed acknowledgment returned within 14 days")

    pdf.h2("8. Intellectual property / liability / term")
    pdf.body(
        "All SPT software, documentation, and service files remain the exclusive "
        "property of SPT and its licensors. No ownership is transferred. "
        "Any authorization, if granted, would be provided as-is; the Applicant "
        "assumes risk of camera modification. Proposed expiration is one year "
        "from the issue date unless extended or earlier revoked for breach."
    )

    pdf.h2("9. Applicant acknowledgment (sign before sending)")
    pdf.body(
        "I have prepared this draft using measured device data from my own camera. "
        "I understand this document has no legal force until an authorized SPT "
        "representative confirms permission in writing. I will not begin "
        "prohibited analysis unless I receive clear written authorization."
    )
    pdf.ln(4)
    pdf.kv("Applicant name:", "________________________________")
    pdf.kv("Signature:", "________________________________")
    pdf.kv("Date:", "________________________________")

    pdf.h2("10. SPT authorization (for SPT only)")
    pdf.body(
        "If SPT grants the limited exception described above, an authorized "
        "representative should complete and sign below (or reply by official "
        "email with an equivalent confirmation and authorization ID)."
    )
    pdf.ln(2)
    pdf.kv("Granted / Denied:", "________________________________")
    pdf.kv("Authorization ID:", "________________________________")
    pdf.kv("Effective / Expiry:", "________________________________")
    pdf.kv("Selected BootFaexe option:", "A / B / C  (circle one)")
    pdf.kv("Name / Title:", "________________________________")
    pdf.kv("Signature / Date:", "________________________________")
    pdf.ln(3)
    pdf.set_font("Body", "I", 9)
    pdf.set_text_color(120, 40, 40)
    pdf.multi_cell(
        0,
        5,
        "NOTICE: This PDF was generated locally by the TornadoEos project as an "
        "applicant draft. It is not an SPT-issued license. Presenting an "
        "unsigned draft as a granted authorization would be false.",
    )

    OUT_PATH.parent.mkdir(parents=True, exist_ok=True)
    pdf.output(str(OUT_PATH))
    return OUT_PATH


if __name__ == "__main__":
    path = build()
    print(path)
    print(f"bytes={path.stat().st_size}")
