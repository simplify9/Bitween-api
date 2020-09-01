using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SW.Infolink;
using SW.Infolink.Model;
using SW.Infolink.Sdk;
using SW.PrimitiveTypes;

namespace SW.Infolink.Web.Pages.Receivers
{
    public class EditModel : PageModel
    {

        public int Id { get; set; }

        [BindProperty]
        public ReceiverConfig Receiver { get; set; }

        [BindProperty]
        public string Properties { get; set; }

        [BindProperty]
        public string Schedules { get; set; }
        public IInfolinkClient ApiService { get; }

        public EditModel(IInfolinkClient apiService)
        {
            ApiService = apiService;
        }


        public async Task<IActionResult> OnPostAsync(int id)
        {

            try
            {

                Receiver.Properties = Properties.ToDictionary();
                Receiver.Schedules = Schedules.ToSchedules(Receiver.Schedules);

                //Receiver receiver;
                if (id != 0)
                {
                    await ApiService.PostAsync($"receivers/{id}/update", Receiver);
                }


                return Redirect("~/Receivers");

            }
            catch (Exception ex)
            {
                ModelState.AddModelError("CustomError", ex.Message);
                return Page();
            }

          
        }
        public async Task OnGetAsync(int id)
        {
            Id = id;
            Receiver = new ReceiverConfig();

            if (id != 0)
            {
                Receiver = await ApiService.GetAsync<ReceiverConfig>($"receivers/{id}");
                Properties = Receiver.Properties.FromDictionary();
                Schedules = Receiver.Schedules.FromSchedules();

            }
        }
    }
}