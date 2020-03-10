﻿using System;
using System.Collections.Generic;
using zapread.com.Models.Database;

namespace zapread.com.Models
{
    public class PostViewModel
    {
        [Obsolete("Use view model attributes instead of the Post object.")]
        public Post Post;

        public string PostTitle;
        public string Content;
        public int PostId;
        public int GroupId;
        public string GroupName;
        public string UserName;
        public int UserId;
        public string UserAppId;
        public int UserProfileImageVersion;
        public int Score;
        public DateTime? TimeStamp { get; set; }
        public DateTime? TimeStampEdited { get; set; }
        public bool IsNSFW;
        public bool IsSticky;

        public IEnumerable<PostCommentsViewModel> CommentVms;
        public bool ViewerIsMod;        // User has moderation privilage on this post
        public bool ViewerUpvoted;      // If the user has upvoted this post
        public bool ViewerDownvoted;    // If the user has downvoted this post
        public bool ViewerIgnoredUser;  // If the user has ignored the user
        public int NumComments;
        public bool IsDetailView;       // If the post is being viewed by itself
        public bool IsFirstPost;        // If the post is the first post on a page

        // Not ideal!
        [Obsolete("Check ViewerIgnoredUser for post status instead.")]
        public List<int> ViewerIgnoredUsers;  // If the user has ignored the user
    }

    public class NewPostViewModel
    {
        public Post Post { get; set; }
        public List<string> Languages { get; set; }
    }

    public class PostsViewModel
    {
        public List<PostViewModel> Posts;
        public List<int> Upvoted;
        public List<int> Downvoted;
        public double UserBalance;
        public List<GroupInfo> SubscribedGroups;
        public string Sort;
    }

    public class NewPostMsg
    {
        public int PostId { get; set; }
        public string Content { get; set; }
        public int GroupId { get; set; }
        public string Title { get; set; }
        public bool IsDraft { get; set; }
        public string Language { get; set; }
    }

    public class PostCommentsViewModel
    {
        [Obsolete]
        public Comment Comment { get; set; }
        [Obsolete]
        public Comment ParentComment { get; set; }

        public PostCommentsViewModel ParentCommentVm { get; set; }
        public List<Comment> Comments { get; set; } // All comments

        [Obsolete("Use ViewerIgnoredUser instead of this list.")]
        public List<int> ViewerIgnoredUsers;        // If the user has ignored the user
        public bool StartVisible { get; set; }
        public int NestLevel { get; set; }          // How far down the comment nesting is

        public string Text { get; set; }

        public int Score { get; set; }

        public bool IsReply { get; set; }

        public bool IsDeleted { get; set; }

        public DateTime? TimeStamp { get; set; }

        public DateTime? TimeStampEdited { get; set; }

        public Int64 CommentId { get; set; }

        public int UserId { get; set; }

        public string UserName { get; set; }

        public string UserAppId { get; set; }
        public int ProfileImageVersion { get; set; }
        public List<PostCommentsViewModel> CommentVms { get; set; } // All comments

        public bool ViewerUpvoted;      // If the user has upvoted this comment
        public bool ViewerDownvoted;    // If the user has downvoted this comment
        public bool ViewerIgnoredUser;  // If the user has ignored the user
    }
}