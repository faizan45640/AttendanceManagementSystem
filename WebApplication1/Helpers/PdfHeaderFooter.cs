using AMS.Services;
using iTextSharp.text;
using iTextSharp.text.pdf;
using Microsoft.AspNetCore.Hosting;
using System.IO;

namespace AMS.Helpers
{
    /// <summary>
    /// PDF page event handler that adds institution header and footer to every page
    /// </summary>
    public class PdfHeaderFooter : PdfPageEventHelper
    {
        private readonly InstitutionInfo _institution;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly Font _headerFont;
        private readonly Font _footerFont;
        private iTextSharp.text.Image? _logoImage;

        public PdfHeaderFooter(InstitutionInfo institution, IWebHostEnvironment webHostEnvironment)
        {
            _institution = institution;
            _webHostEnvironment = webHostEnvironment;
            _headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12);
            _footerFont = FontFactory.GetFont(FontFactory.HELVETICA, 8, BaseColor.GRAY);

            // Try to load logo
            LoadLogo();
        }

        private void LoadLogo()
        {
            try
            {
                if (!string.IsNullOrEmpty(_institution.Logo))
                {
                    var logoPath = Path.Combine(_webHostEnvironment.WebRootPath, _institution.Logo.TrimStart('/'));
                    if (File.Exists(logoPath))
                    {
                        _logoImage = iTextSharp.text.Image.GetInstance(logoPath);
                        _logoImage.ScaleToFit(40f, 40f);
                    }
                }
            }
            catch
            {
                _logoImage = null;
            }
        }

        public override void OnOpenDocument(PdfWriter writer, Document document)
        {
            base.OnOpenDocument(writer, document);
        }

        public override void OnStartPage(PdfWriter writer, Document document)
        {
            base.OnStartPage(writer, document);

            // Add header with institution info
            PdfPTable headerTable = new PdfPTable(2);
            headerTable.TotalWidth = document.PageSize.Width - document.LeftMargin - document.RightMargin;
            headerTable.SetWidths(new float[] { 1f, 5f });

            // Logo cell
            PdfPCell logoCell;
            if (_logoImage != null)
            {
                logoCell = new PdfPCell(_logoImage);
            }
            else
            {
                // Fallback: text-based logo placeholder
                logoCell = new PdfPCell(new Phrase("", _headerFont));
            }
            logoCell.Border = Rectangle.NO_BORDER;
            logoCell.VerticalAlignment = Element.ALIGN_MIDDLE;
            logoCell.PaddingBottom = 10;
            headerTable.AddCell(logoCell);

            // Institution name cell
            PdfPCell nameCell = new PdfPCell();
            nameCell.Border = Rectangle.NO_BORDER;
            nameCell.VerticalAlignment = Element.ALIGN_MIDDLE;
            nameCell.PaddingBottom = 10;

            Paragraph institutionName = new Paragraph(_institution.Name, _headerFont);
            nameCell.AddElement(institutionName);

            if (!string.IsNullOrEmpty(_institution.Address))
            {
                Paragraph address = new Paragraph(_institution.Address, FontFactory.GetFont(FontFactory.HELVETICA, 8, BaseColor.GRAY));
                nameCell.AddElement(address);
            }

            headerTable.AddCell(nameCell);

            // Write header
            headerTable.WriteSelectedRows(0, -1, document.LeftMargin, document.PageSize.Height - 15, writer.DirectContent);

            // Add a separator line - positioned lower to avoid clashing with content
            PdfContentByte cb = writer.DirectContent;
            cb.SetColorStroke(new BaseColor(229, 231, 235));
            cb.SetLineWidth(0.5f);
            cb.MoveTo(document.LeftMargin, document.PageSize.Height - 60);
            cb.LineTo(document.PageSize.Width - document.RightMargin, document.PageSize.Height - 60);
            cb.Stroke();
        }

        public override void OnEndPage(PdfWriter writer, Document document)
        {
            base.OnEndPage(writer, document);

            // Footer
            PdfPTable footerTable = new PdfPTable(2);
            footerTable.TotalWidth = document.PageSize.Width - document.LeftMargin - document.RightMargin;
            footerTable.SetWidths(new float[] { 3f, 1f });

            // Institution info cell
            string footerText = _institution.Name;
            if (!string.IsNullOrEmpty(_institution.Phone))
                footerText += $" | Tel: {_institution.Phone}";
            if (!string.IsNullOrEmpty(_institution.Email))
                footerText += $" | {_institution.Email}";

            PdfPCell infoCell = new PdfPCell(new Phrase(footerText, _footerFont));
            infoCell.Border = Rectangle.TOP_BORDER;
            infoCell.BorderColor = new BaseColor(229, 231, 235);
            infoCell.PaddingTop = 5;
            infoCell.HorizontalAlignment = Element.ALIGN_LEFT;
            footerTable.AddCell(infoCell);

            // Page number cell
            PdfPCell pageCell = new PdfPCell(new Phrase($"Page {writer.PageNumber}", _footerFont));
            pageCell.Border = Rectangle.TOP_BORDER;
            pageCell.BorderColor = new BaseColor(229, 231, 235);
            pageCell.PaddingTop = 5;
            pageCell.HorizontalAlignment = Element.ALIGN_RIGHT;
            footerTable.AddCell(pageCell);

            // Write footer at the bottom
            footerTable.WriteSelectedRows(0, -1, document.LeftMargin, 35, writer.DirectContent);
        }
    }
}
