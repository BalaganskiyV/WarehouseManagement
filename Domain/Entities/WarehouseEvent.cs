using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Entities
{
    public class WarehouseEvent
    {
        public int Id { get; set; }
        public string EventType { get; set; } = "";
        public int ProductId { get; set; }
        public string ProductName { get; set; } = "";
        public int Quantity { get; set; }
        public string WarehouseZone { get; set; } = "";
        public int UserId { get; set; }
        public DateTime EventDate { get; set; }
        public string Description { get; set; } = "";
    }
}