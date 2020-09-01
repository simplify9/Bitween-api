using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.FileProviders;
using SW.HttpExtensions;
using SW.Infolink;
using SW.Infolink.Api.Resources.Blobs;
using SW.Infolink.Domain;
using SW.Infolink.Model;
using SW.Infolink.Sdk;

namespace SW.Infolink.Web.Pages
{
    public class DownloadModel : PageModel
    {

        public IInfolinkClient ApiService { get; }

        public DownloadModel(IInfolinkClient apiService)
        {

            ApiService = apiService;
        }


        public async Task<IActionResult> OnGetAsync(int id, XchangeFileType type)
        {

            var rs = await ApiService.GetAsync<XchangeBlobDto>($"blobs?id={id}&type={(int)type}");
            var xchange = await ApiService.GetAsync<XchangeRow>($"xchanges/{id}");

            return File(
             Encoding.UTF8.GetBytes(rs.Content), "application/octet-stream", $"{(xchange.InputFileName != "" ? xchange.InputFileName : "") + "-" + id + "-" + type.ToString()}.txt");

        }

    }
}