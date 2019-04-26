﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace zapread.com.Models.UserViews
{
    public class UserHoverViewModel
    {
        public zapread.com.Models.Database.User User { get; set; }
        public bool IsFollowing { get; set; }
        public bool IsIgnored { get; set; }
    }
}