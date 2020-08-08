using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using SW.Infolink;
using SW.Infolink.Sdk;
using SW.PrimitiveTypes;

namespace SW.Infolink.Web.Pages.Adapters
{
    public class EditModel : PageModel
    {

        public int Id { get; set; }


        [BindProperty]
        public AdapterConfig Adapter { get; set; }

        [BindProperty]
        public string Properties { get; set; }

        [BindProperty]
        [Required]
        public IFormFile Upload { get; set; }

        public IInfolinkClient ApiService { get; }

        public EditModel(IInfolinkClient apiService)
        {
            ApiService = apiService;
        }


        public async Task<IActionResult> OnPostAsync(int id)
        {
            try
            {

                //if (Upload != null)
                //{
                //    using (var ms = new MemoryStream())
                //    {
                //        await Upload.CopyToAsync(ms);
                //        Adapter.Package = ms.ToArray();
                //    }
                //}


                Adapter.Properties = Properties.ToDictionary();

                if (id != 0)
                {
                    await ApiService.PostAsync($"adapters/{id}/update", Adapter);
                }
                else
                {
                    id = await ApiService.PostAsync<int>($"adapters", Adapter);
                }
                return Redirect("~/Adapters");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("CustomError", ex.Message);
                return Page();
            }


        }
        public async Task OnGetAsync(int id)
        {
            if (id == 0)
                Adapter = new AdapterConfig();
                
            else
                Adapter = await ApiService.GetAsync<AdapterConfig>($"adapters/{id}");

            Properties = Adapter.Properties.FromDictionary();


        }
    }
}